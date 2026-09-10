using System.Windows;
using System.Windows.Input;

namespace MineRailMonitor.Pages;

public partial class DeleteMapAnnotationDialog : Window
{
    public DeleteMapAnnotationDialog(string annotationName)
    {
        InitializeComponent();
        AnnotationNameText.Text = string.IsNullOrWhiteSpace(annotationName) ? "未命名标注" : annotationName;
    }

    public bool Confirmed { get; private set; }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
