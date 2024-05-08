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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;

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
        MediaCapture mediaCapture;

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
            toggleButton_camera.IsChecked = false;
            mediaCapture.Dispose();
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
                    anchor.BorderBrush = new SolidColorBrush(Anchor.class2Color[result.className]);
                    anchor.BorderThickness = new Thickness(2);
                    anchor.PointerEntered += (s, e) =>
                    {
                        Dictionary<string, string> class2Name = new()
                        {
                            { "D00", "纵向裂缝" },
                            { "D10", "横向裂缝" },
                            { "D20", "鳄鱼裂缝" },
                            { "D30", "坑洼" }
                        };
                        textBlock_conf.Text = $"confidence：{result.confidence * 100:f2}%";
                        textBlock_class.Text = $"class          ：{class2Name[result.className]}";
                        textBlock_x1.Text = $"x1              ：{result.x1 / zoom:f0}";
                        textBlock_x2.Text = $"x2              ：{result.x2 / zoom:f0}";
                        textBlock_y1.Text = $"y1              ：{result.y1 / zoom:f0}";
                        textBlock_y2.Text = $"y2              ：{result.y2 / zoom:f0}";
                    };

                    canvas.Children.Add(new Border());
                    var label = canvas.Children.Last() as Border;
                    Canvas.SetLeft(label, x + result.x1);
                    Canvas.SetTop(label, result.y1 - 20);
                    Canvas.SetZIndex(label, 1);
                    label.Background = new SolidColorBrush(Anchor.class2Color[result.className]);
                    label.Child = new TextBlock();
                    (label.Child as TextBlock).Foreground = new SolidColorBrush(Colors.White);
                    (label.Child as TextBlock).Text = $"{result.confidence:f2} {result.className})";
                }
            }
        }

        private async void toggleButton_camera_Click(object sender, RoutedEventArgs e)
        {
            toggleButton_start.IsChecked = false;
            if ((bool)toggleButton_camera.IsChecked)
            {
                var groups = await MediaFrameSourceGroup.FindAllAsync();
                if (groups.Count == 0)
                {
                    toggleButton_camera.IsEnabled = false;
                    Debug.WriteLine("没找到摄像机，你个瓜货");
                    return;
                }
                var mediaFrameSourceGroup = groups[0];

                Debug.WriteLine("摄像机名字: " + mediaFrameSourceGroup.DisplayName);
                mediaCapture = new MediaCapture();
                var mediaCaptureInitializationSettings = new MediaCaptureInitializationSettings()
                {
                    SourceGroup = mediaFrameSourceGroup,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu
                };
                await mediaCapture.InitializeAsync(mediaCaptureInitializationSettings);

                // Set the MediaPlayerElement's Source property to the MediaSource for the mediaCapture.
                var frameSource = mediaCapture.FrameSources[mediaFrameSourceGroup.SourceInfos[0].Id];

                MediaPlayerElement captureElement = new()
                {
                    Stretch = Stretch.Uniform,
                    AutoPlay = true,
                };
                border_image.Child = captureElement;
                // bing : 尝试在布局更新后再设置 MediaPlayerElement 的源。这可以确保控件已经被添加到布局中，并且有足够的信息来确定其大小。
                captureElement.Source = Windows.Media.Core.MediaSource.CreateFromMediaFrameSource(frameSource);

                toggleButton_start.IsEnabled = false;
            }
            else
            {
                // Capture a photo to a stream
                var imgFormat = ImageEncodingProperties.CreateJpeg();
                var stream = new InMemoryRandomAccessStream();
                await mediaCapture.CapturePhotoToStreamAsync(imgFormat, stream);
                stream.Seek(0);

                // Save the photo to the Pictures folder
                var file = await KnownFolders.PicturesLibrary.CreateFileAsync("CRDDC.jpg", CreationCollisionOption.GenerateUniqueName);
                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    await RandomAccessStream.CopyAndCloseAsync(stream.GetInputStreamAt(0), fileStream.GetOutputStreamAt(0));

                // Show the photo in an Image element
                BitmapImage bmpImage = new();
                await bmpImage.SetSourceAsync(stream);
                var image = new Image() 
                { 
                    Source = bmpImage,
                    Stretch = Stretch.Uniform,
                    Width = border_image.ActualWidth,
                    Height = border_image.ActualHeight
                };

                target = new(file.Path)
                {
                    imageWidth = bmpImage.PixelWidth,
                    imageHeight = bmpImage.PixelHeight
                };

                border_image.Child = new Canvas();
                var canvas = border_image.Child as Canvas;
                canvas.Children.Add(image);
                Canvas.SetZIndex(canvas.Children[0], 0);
                toggleButton_start.IsEnabled = true;

                toggleButton_start.IsChecked = false;
                mediaCapture.Dispose();

            }
        }
    }
}
