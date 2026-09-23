using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Recognition;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorProtocolTests
{
    [Fact]
    public void StationCatalogProvidesSixUnassignedIndependentSlots()
    {
        var catalog = new SimulatorStationCatalog();

        Assert.Equal(6, catalog.Capacity);
        Assert.Empty(catalog.ConfiguredStations);
        for (var index = 0; index < catalog.Capacity; index++)
        {
            catalog.Set(index, new SimulatorStation { Address = (byte)(index + 1) });
        }

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, catalog.ConfiguredStations.Select(station => station.Address));
    }

    [Fact]
    public void Responder_RoutesSixIndependentStationsByRequestAddress()
    {
        var stations = Enumerable.Range(1, 6)
            .Select(index => new SimulatorStation
            {
                Address = (byte)index,
                Slots = CreateRfids((ushort)(1000 + index), (ushort)(2000 + index)),
                CommandBytes = new byte[] { (byte)index, 0, 0, 0 }
            })
            .ToArray();
        var responder = new RfidSimulatorResponder(stations, emptySlotValue: 0);

        for (var index = 1; index <= 6; index++)
        {
            var response = responder.CreateResponse(CreateRequest((byte)index));

            Assert.NotNull(response);
            Assert.Equal((byte)index, response![2]);
            Assert.Equal((byte)index, response[4]);
            Assert.Equal((ushort)(1000 + index), ReadLittleEndian(response, 8));
            Assert.Equal((ushort)(2000 + index), ReadLittleEndian(response, 10));
        }
    }

    [Fact]
    public void Responder_UnknownAddress_ReturnsNoBusinessData()
    {
        var responder = new RfidSimulatorResponder(new[]
        {
            new SimulatorStation { Address = 0x01, Slots = CreateRfids(1001) }
        }, emptySlotValue: 0);

        Assert.Null(responder.CreateResponse(CreateRequest(0x7F)));
    }

    [Fact]
    public async Task UdpResponder_ReceivesRequestAndRepliesToSender()
    {
        var responder = new RfidSimulatorResponder(new[]
        {
            new SimulatorStation
            {
                Address = 0x03,
                Slots = CreateRfids(new[] { (ushort)0x0001 }.Concat(Enumerable.Range(0x0011, 10).Select(value => (ushort)value)).ToArray()),
                CommandBytes = new byte[] { 1, 2, 3, 4 }
            }
        }, emptySlotValue: 0);
        using var server = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        using var cancellation = new CancellationTokenSource();
        var serverTask = server.RunAsync(responder.CreateResponse, cancellation.Token);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var request = CreateRequest(0x03);
        await client.SendAsync(request, request.Length, server.LocalEndPoint);
        var response = await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(3));

        Assert.Equal(40, response.Length);
        Assert.Equal((ushort)0x0001, ReadLittleEndian(response, 8));
        Assert.Equal((ushort)0x001A, ReadLittleEndian(response, 28));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0 }, response.Skip(30).Take(6));

        cancellation.Cancel();
        await serverTask;
    }

    [Fact]
    public async Task Async_responder_cancellation_with_pending_delay_completes_cleanly()
    {
        using var server = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        using var cancellation = new CancellationTokenSource();
        var responseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAsync(
            async (request, _, token) =>
            {
                responseStarted.TrySetResult(true);
                var pendingResponse = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                var delayedResponse = Task.Delay(TimeSpan.FromSeconds(5));
                using (token.Register(() =>
                {
                    pendingResponse.TrySetCanceled();
                    Thread.Sleep(100);
                }))
                {
                    await Task.WhenAny(delayedResponse, pendingResponse.Task);
                    return await pendingResponse.Task;
                }
            },
            cancellation.Token);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var request = CreateRequest(0x03);
        await client.SendAsync(request, request.Length, server.LocalEndPoint);
        var signalCompleted = await Task.WhenAny(responseStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(responseStarted.Task, signalCompleted);

        cancellation.Cancel();
        var exception = await Record.ExceptionAsync(() => serverTask);

        Assert.Null(exception);
    }

    [Fact]
    public async Task Cancellation_during_pending_response_send_completes_responder_cleanly()
    {
        using var server = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        using var cancellation = new CancellationTokenSource();
        var factoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAsync(
            async (request, _, _) =>
            {
                factoryEntered.TrySetResult(true);
                await releaseResponse.Task.ConfigureAwait(false);
                return new[] { request[2] };
            },
            cancellation.Token);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var request = CreateRequest(0x01);
        await client.SendAsync(request, request.Length, server.LocalEndPoint);
        await factoryEntered.Task;

        cancellation.Cancel();
        releaseResponse.TrySetResult(true);
        await serverTask;
    }

    [Fact]
    public async Task Async_responder_continues_receiving_while_previous_response_is_delayed()
    {
        using var server = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        using var cancellation = new CancellationTokenSource();
        var firstResponseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAsync(
            async (request, _, token) =>
            {
                if (request[2] == 0x01)
                {
                    firstResponseStarted.TrySetResult(true);
                    await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                }

                return new[] { request[2] };
            },
            cancellation.Token);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var firstRequest = CreateRequest(0x01);
        await client.SendAsync(firstRequest, firstRequest.Length, server.LocalEndPoint);
        var signalCompleted = await Task.WhenAny(firstResponseStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(firstResponseStarted.Task, signalCompleted);

        var secondRequest = CreateRequest(0x02);
        await client.SendAsync(secondRequest, secondRequest.Length, server.LocalEndPoint);
        var secondResponse = await ReceiveWithTimeoutAsync(client, TimeSpan.FromMilliseconds(250));
        Assert.Equal(new byte[] { 0x02 }, secondResponse);

        var firstResponse = await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(2));
        Assert.Equal(new byte[] { 0x01 }, firstResponse);

        cancellation.Cancel();
        await serverTask;
    }

    [Fact]
    public void Build_Returns40BytesWithProtocolMarkers()
    {
        var input = new SimulatorFrameInput
        {
            Address = 0x03,
            CommandBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 },
            Slots = CreateRfids(0x1234, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x2010, 0x2111, 0x2222, 0x3333)
        };

        var frame = RfidResponseFrameBuilder.Build(input);

        Assert.Equal(40, frame.Length);
        Assert.Equal(0xB0, frame[0]);
        Assert.Equal(0xB0, frame[1]);
        Assert.Equal(0x03, frame[2]);
        Assert.Equal(0x04, frame[3]);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x0E }, frame.Skip(4).Take(4));
        Assert.Equal(0x0E, frame[7]);
        Assert.Equal(0x34, frame[8]);
        Assert.Equal(0x12, frame[9]);
        Assert.Equal(0x33, frame[35]);
        Assert.Equal(0x00, frame[36]);
        Assert.Equal(0x00, frame[37]);
        Assert.Equal(0xAA, frame[38]);
        Assert.Equal(0xAA, frame[39]);
    }

    [Fact]
    public void TryParse_AcceptsDecimalAndHex()
    {
        Assert.True(RfidValueParser.TryParse("1001", out var decimalValue));
        Assert.Equal((ushort)1001, decimalValue);

        Assert.True(RfidValueParser.TryParse("0x03E9", out var hexValue));
        Assert.Equal((ushort)1001, hexValue);
    }

    [Fact]
    public void TryParse_RejectsOutOfRange()
    {
        Assert.False(RfidValueParser.TryParse("70000", out _));
    }

    [Fact]
    public void TryParse_AcceptsBareFourDigitHex()
    {
        Assert.True(RfidValueParser.TryParse("001D", out var value));
        Assert.Equal((ushort)0x001D, value);
        Assert.True(RfidValueParser.TryParse("0011", out var numericHexValue));
        Assert.Equal((ushort)0x0011, numericHexValue);
        Assert.True(RfidValueParser.TryParse("0001", out var headValue));
        Assert.Equal((ushort)0x0001, headValue);
    }

    [Fact]
    public async Task UdpEndToEnd_ParsesFourteenSlotsAndAddsOnlyNewRfids()
    {
        var station = new SimulatorStation { Address = 0x01, Slots = new ushort[14] };
        var responder = new RfidSimulatorResponder(new[] { station }, emptySlotValue: 0);
        var tracker = new StationRecognitionTracker(11, TimeSpan.FromSeconds(30));
        using var server = new SimulatorUdpResponder(IPAddress.Loopback, 0);
        using var cancellation = new CancellationTokenSource();
        var serverTask = server.RunAsync(responder.CreateResponse, cancellation.Token);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var parser = new RfidFrameParser();

        var snapshots = new[]
        {
            new ushort[] { },
            new ushort[] { 0x001A },
            new ushort[] { 0x001A, 0x000E },
            new ushort[] { 0x001A, 0x000E, 0x0021, 0x001D, 0x0017 },
            new ushort[] { 0x001A, 0x000E, 0x0021, 0x001D, 0x0017 },
            new ushort[] { 0x001A, 0x000E, 0x0021, 0x001D, 0x0017, 0x001F }
        };

        var now = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.FromHours(8));
        foreach (var snapshot in snapshots)
        {
            Array.Clear(station.Slots, 0, station.Slots.Length);
            for (var index = 0; index < snapshot.Length; index++)
            {
                station.Slots[(index * 2) % 14] = snapshot[index];
            }

            var request = CreateRequest(0x01);
            await client.SendAsync(request, request.Length, server.LocalEndPoint);
            var response = await ReceiveWithTimeoutAsync(client, TimeSpan.FromSeconds(3));
            Assert.True(parser.TryParse(response, (IPEndPoint)server.LocalEndPoint, now, out var frame));
            Assert.NotNull(frame);
            Assert.Equal(snapshot.Length, frame!.ActualNonZeroSlotCount);
            tracker.Apply(frame);
            now = now.AddMilliseconds(200);
        }

        Assert.Equal(6, tracker.Sessions[0x01].DetectedVehicleCount);
        Assert.Equal(new ushort[] { 0x001A, 0x000E, 0x0021, 0x001D, 0x0017, 0x001F }, tracker.Sessions[0x01].ObservedVehicleSequence);

        cancellation.Cancel();
        await serverTask;
    }

    [Fact]
    public void ConfiguredExpectedVehicleCount_ChangesCompletionThreshold()
    {
        var tracker = new StationRecognitionTracker(8, TimeSpan.FromSeconds(30));
        var frame = CreateTrainInput(7);
        var response = RfidResponseFrameBuilder.Build(frame);
        Assert.True(new RfidFrameParser().TryParse(response, new IPEndPoint(IPAddress.Loopback, 62001), DateTimeOffset.Now, out var parsed));
        Assert.Equal(8, tracker.Apply(parsed!).DetectedVehicleCount);
        Assert.Equal(StationRecognitionState.Completed, tracker.Sessions[0x03].State);
    }

    [Fact]
    public async Task UdpLoopback_NormalTenWagons_IsOne40ByteDatagram()
    {
        var input = CreateTrainInput(10);
        var expected = RfidResponseFrameBuilder.Build(input);

        using var receiver = CreateReceiver(out var receiverEndPoint);
        using var transport = new SimulatorUdpTransport(IPAddress.Loopback, 0, IPAddress.Loopback, receiverEndPoint.Port);

        await transport.SendAsync(expected, CancellationToken.None);
        var actual = await ReceiveWithTimeoutAsync(receiver, TimeSpan.FromSeconds(3));

        Assert.Equal(40, actual.Length);
        Assert.Equal(expected, actual);
        Assert.Equal(11, input.Slots.Count(value => value != input.EmptySlotValue));
        Assert.Equal((ushort)0x0001, input.Slots[0]);
        Assert.Equal((ushort)0x0011, input.Slots[1]);
        Assert.Equal((ushort)0x001A, input.Slots[10]);
        Assert.All(input.Slots.Skip(11), value => Assert.Equal(input.EmptySlotValue, value));
    }

    [Fact]
    public async Task UdpLoopback_SixWagons_UsesEmptySlotsAndRemains40Bytes()
    {
        var input = CreateTrainInput(6);
        var expected = RfidResponseFrameBuilder.Build(input);

        using var receiver = CreateReceiver(out var receiverEndPoint);
        using var transport = new SimulatorUdpTransport(IPAddress.Loopback, 0, IPAddress.Loopback, receiverEndPoint.Port);

        await transport.SendAsync(expected, CancellationToken.None);
        var actual = await ReceiveWithTimeoutAsync(receiver, TimeSpan.FromSeconds(3));

        Assert.Equal(40, actual.Length);
        Assert.Equal(expected, actual);
        Assert.Equal(new byte[] { 0x00, 0x00 }, actual.Skip(22).Take(2).ToArray());
        Assert.Equal(7, input.Slots.Count(value => value != input.EmptySlotValue));
        Assert.Equal((ushort)0x0001, input.Slots[0]);
        Assert.Equal((ushort)0x0011, input.Slots[1]);
        Assert.Equal((ushort)0x0016, input.Slots[6]);
        Assert.All(input.Slots.Skip(7), value => Assert.Equal(input.EmptySlotValue, value));
    }

    [Fact]
    public void Responder_ClearCommand_ClearsAllFourteenSlotsUntilTagsAreWrittenAgain()
    {
        var station = new SimulatorStation { Address = 0x01, Slots = CreateRfids(0x001A, 0x001D) };
        var responder = new RfidSimulatorResponder(new[] { station }, emptySlotValue: 0);

        var response = responder.CreateResponse(CreateRequest(0x01));
        Assert.Equal((byte)2, response![7]);

        var clearRequest = CreateRequest(0x01);
        clearRequest[4] = 0x01;
        var cleared = responder.CreateResponse(clearRequest);

        Assert.NotNull(cleared);
        Assert.Equal(0, cleared![7]);
        Assert.All(Enumerable.Range(0, 14), index => Assert.Equal((ushort)0, ReadLittleEndian(cleared, 8 + index * 2)));

        station.Slots[3] = 0x0021;
        var reappeared = responder.CreateResponse(CreateRequest(0x01));
        Assert.Equal((byte)1, reappeared![7]);
        Assert.Equal((ushort)0x0021, ReadLittleEndian(reappeared, 8 + 3 * 2));
    }

    [Fact]
    public void Responder_PreservesSparseFourteenSlotPositions()
    {
        var station = new SimulatorStation { Address = 0x01, Slots = CreateRfidsAt((13, (ushort)0x001D), (3, (ushort)0x001A)) };
        var response = new RfidSimulatorResponder(new[] { station }, emptySlotValue: 0)
            .CreateResponse(CreateRequest(0x01));

        Assert.NotNull(response);
        Assert.Equal((byte)2, response![7]);
        Assert.Equal((ushort)0x001A, ReadLittleEndian(response, 8 + 3 * 2));
        Assert.Equal((ushort)0x001D, ReadLittleEndian(response, 8 + 13 * 2));
    }

    private static UdpClient CreateReceiver(out IPEndPoint endPoint)
    {
        var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        endPoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
        return receiver;
    }

    private static async Task<byte[]> ReceiveWithTimeoutAsync(UdpClient receiver, TimeSpan timeout)
    {
        var receiveTask = receiver.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(timeout));
        if (completed != receiveTask)
        {
            throw new TimeoutException("UDP loopback datagram was not received within the test timeout.");
        }

        return (await receiveTask).Buffer;
    }

    private static SimulatorFrameInput CreateTrainInput(int wagonCount)
    {
        var rfids = new ushort[14];
        for (var index = 0; index < rfids.Length; index++)
        {
            rfids[index] = 0x0000;
        }

        rfids[0] = 0x0001;
        for (var index = 1; index <= wagonCount; index++)
        {
            rfids[index] = (ushort)(0x0010 + index);
        }

        return new SimulatorFrameInput
        {
            Address = 0x03,
            CommandBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 },
            EmptySlotValue = 0x0000,
            Slots = rfids,
            CrcHigh = 0x12,
            CrcLow = 0x34
        };
    }

    private static ushort[] CreateRfids(params ushort[] values)
    {
        var rfids = new ushort[14];
        Array.Copy(values, rfids, values.Length);
        return rfids;
    }

    private static ushort[] CreateRfidsAt(params (int Index, ushort Value)[] values)
    {
        var rfids = new ushort[14];
        foreach (var value in values)
        {
            rfids[value.Index] = value.Value;
        }

        return rfids;
    }

    private static byte[] CreateRequest(byte address)
    {
        var request = new byte[40];
        request[0] = 0xB0;
        request[1] = 0xB0;
        request[2] = address;
        request[3] = 0x04;
        request[38] = 0xAA;
        request[39] = 0xAA;
        return request;
    }

    private static ushort ReadLittleEndian(byte[] buffer, int offset) =>
        (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
}
