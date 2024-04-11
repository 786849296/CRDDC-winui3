using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;

namespace CRDDC
{
    public class Images(string path)
    {
        [ColumnName("images")]
        public string imagePath = path;

        public float imageWidth;
        public float imageHeight;
    }

    public class Output
    {
        [ColumnName("output")]
        public float[] labels;
    }

    public class Anchor
    {
        public const int classNum = 4;
        public static readonly string[] classes = ["D00", "D10", "D20", "D30"];

        public static readonly Dictionary<string, Color> class2Color = new() 
        {
            { "D00", Colors.Red },
            { "D10", Colors.Pink },
            { "D20", Colors.Orange },
            { "D30", Colors.Yellow }
        };

        public float x1;
        public float y1;
        public float x2;
        public float y2;
        public float confidence;
        public string className;

        public static float IOU(Anchor a, Anchor b)
        {
            var areaA = (a.x2 - a.x1) * (a.y2 - a.y1);
            var areaB = (b.x2 - b.x1) * (b.y2 - b.y1);
            var minX = Math.Max(a.x1, b.x1);
            var minY = Math.Max(a.y1, b.y1);
            var maxX = Math.Min(a.x2, b.x2);
            var maxY = Math.Min(a.y2, b.y2);
            var intersectionArea = Math.Max(maxX - minX, 0) * Math.Max(maxY - minY, 0);
            return intersectionArea / (areaA + areaB - intersectionArea);
        }

        // Convert from yolov5-master/utils/general.py/non_max_suppression()
        // But according to the only steps predicted by YPL, the code can be deleted and cut
        public static List<Anchor> NMS(Output pred, double conf_thres, double iou_thres, int max_det = 300)
        {
            List<int> confI = [];
            for (int i = 4; i < pred.labels.Length; i += 9)
                if (pred.labels[i] > conf_thres)
                    confI.Add(i);
            List<Anchor> output = [];
            foreach (int i in confI)
            {
                float[] classConf = [pred.labels[i + 1], pred.labels[i + 2], pred.labels[i + 3], pred.labels[i + 4]];
                Anchor anchor = new()
                {
                    x1 = pred.labels[i - 4] - pred.labels[i - 2] / 2,
                    y1 = pred.labels[i - 3] - pred.labels[i - 1] / 2,
                    x2 = pred.labels[i - 4] + pred.labels[i - 2] / 2,
                    y2 = pred.labels[i - 3] + pred.labels[i - 1] / 2,
                    confidence = pred.labels[i] * classConf.Max(),
                    className = classes[Array.IndexOf(classConf, classConf.Max())]
                };
                if (anchor.confidence < conf_thres || anchor.x2 - anchor.x1 < 10 || anchor.y2 - anchor.y1 < 10)
                    continue;
                output.Add(anchor);
            }
            // Convert from https://zh.d2l.ai/chapter_computer-vision/anchor.html#subsec-predicting-bounding-boxes-nms
            output.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            for (int i = 1; i < Math.Min(output.Count, max_det); i++)
                for (int j = 0; j < i; j++)
                    if (IOU(output[i], output[j]) > iou_thres)
                    {
                        output.RemoveAt(i);
                        i--;
                        break;
                    }
            return output.GetRange(0, Math.Min(output.Count, max_det));
        }
    }

    class YPLNet : IDisposable
    {
        private readonly MLContext mlContext = new();
        public PredictionEngine<Images, Output> predictor;

        public static string GetAbsolutePath(string relativePath)
        {
            FileInfo _dataRoot = new(typeof(Program).Assembly.Location);
            string assemblyFolderPath = _dataRoot.Directory.FullName;
            string fullPath = Path.Combine(assemblyFolderPath, relativePath);
            return fullPath;
        }

        public YPLNet(string modelLocation)
        {
            var data = mlContext.Data.LoadFromEnumerable(new List<Images>());
            var pipeline = mlContext.Transforms.LoadImages("images", "", "images")
                .Append(mlContext.Transforms.ResizeImages("images", 1280, 1280, "images", Microsoft.ML.Transforms.Image.ImageResizingEstimator.ResizingKind.IsoPad))
                //真的想给你两坨子，scaleImage, 一点都不明确
                .Append(mlContext.Transforms.ExtractPixels("images", scaleImage: 1 / 255f))
                .Append(mlContext.Transforms.ApplyOnnxModel(GetAbsolutePath(modelLocation)));
            var model = pipeline.Fit(data);
            predictor = mlContext.Model.CreatePredictionEngine<Images, Output>(model);
        }

        public List<Anchor> predict(Images x, int imgsz = 1280)
        {
            var output = predictor.Predict(x);
            var anchors = Anchor.NMS(output, 0.09, 0.5);
            foreach (var anchor in anchors)
            {
                var gain = Math.Min(x.imageWidth / imgsz, x.imageHeight / imgsz);
                var padW = (x.imageWidth - imgsz * gain) / 2;
                var padH = (x.imageHeight - imgsz * gain) / 2;
                anchor.x1 = (anchor.x1 - padW) * gain;
                anchor.x2 = (anchor.x2 - padW) * gain;
                anchor.y1 = (anchor.y1 - padH) * gain;
                anchor.y2 = (anchor.y2 - padH) * gain;

                anchor.x1 = Math.Max(0, Math.Min(anchor.x1, x.imageWidth));
                anchor.x2 = Math.Max(0, Math.Min(anchor.x2, x.imageWidth));
                anchor.y1 = Math.Max(0, Math.Min(anchor.y1, x.imageHeight));
                anchor.y2 = Math.Max(0, Math.Min(anchor.y2, x.imageHeight));
            }
            return anchors;
        }

        public void Dispose()
        {
            predictor.Dispose();
        }
    }
}
