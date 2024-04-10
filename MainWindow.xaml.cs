using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CRDDC
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        readonly YPLNet model = new("Assets\\32-1280-140.onnx");
        Images target;

        public MainWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
        }

        private void border_image_DragEnter(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
                e.AcceptedOperation = DataPackageOperation.Copy;
            else
                e.AcceptedOperation = DataPackageOperation.None;
        }
        private async void border_image_Drop(object sender, DragEventArgs e)
        {
            var border = sender as Border;
            var items = await e.DataView.GetStorageItemsAsync();
            var storageFile = items[0] as StorageFile;
            string[] imageExtensions = [".jpg", ".jpeg", ".png"];
            if (!imageExtensions.Any(ext => storageFile.FileType == ext))
                return;
            target = new(storageFile.Path);
            BitmapImage bitmap = new();
            bitmap.SetSource(await storageFile.OpenAsync(FileAccessMode.Read));
            target.imageHeight = bitmap.PixelHeight;
            target.imageWidth = bitmap.PixelWidth;
            if (border.Child is not Canvas)
            {
                border.Child = new Canvas();
                var canvas = border.Child as Canvas;
                canvas.Children.Add(new Image());
                (canvas.Children[0] as Image).Stretch = Stretch.Uniform;
                Canvas.SetZIndex(canvas.Children[0], 0);
                toggleButton_start.IsEnabled = true;
            }
            var image = (border.Child as Canvas).Children[0] as Image;
            image.Width = border.ActualWidth;
            image.Height = border.ActualHeight;
            image.Source = bitmap;
            for (int i = (border.Child as Canvas).Children.Count - 1; i > 0; i--)
                (border.Child as Canvas).Children.RemoveAt(i);
            toggleButton_start.IsChecked = false;
        }

        private void toggleButton_start_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)(sender as ToggleButton).IsChecked)
            {
                var results = model.predict(target);
                var canvas = border_image.Child as Canvas;
                foreach (var result in results)
                {
                    canvas.Children.Add(new Border());
                    var anchor = canvas.Children.Last() as Border;
                    var zoom = canvas.ActualHeight / target.imageHeight;
                    var imageActualWidth = target.imageWidth * zoom;
                    result.x1 *= (float)zoom;
                    result.x2 *= (float)zoom;
                    result.y1 *= (float)zoom;
                    result.y2 *= (float)zoom;
                    var x = (canvas.ActualWidth - imageActualWidth) / 2;
                    Canvas.SetLeft(anchor, x + result.x1);
                    Canvas.SetTop(anchor, result.y1);
                    Canvas.SetZIndex(anchor, 1);
                    anchor.Width = result.x2 - result.x1;
                    anchor.Height = result.y2 - result.y1;
                    anchor.BorderBrush = new SolidColorBrush(result.getBoxColor());
                    anchor.BorderThickness = new Thickness(2);

                    canvas.Children.Add(new Border());
                    var label = canvas.Children.Last() as Border;
                    Canvas.SetLeft(label, x + result.x1);
                    Canvas.SetTop(label, result.y1 - 20);
                    Canvas.SetZIndex(label, 1);
                    label.Background = new SolidColorBrush(result.getBoxColor());
                    label.Child = new TextBlock();
                    (label.Child as TextBlock).Foreground = new SolidColorBrush(Colors.White);
                    (label.Child as TextBlock).Text = $"{result.confidence:f2} {result.className})";
                }
            }
        }
    }
}
