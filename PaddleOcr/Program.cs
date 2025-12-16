namespace PaddleOcr;

using OpenCvSharp;

internal class Program
{
    static void Main(string[] args)
    {
        using var engine = new PaddleOcrEngine();
        var filenames = Directory.GetFiles("Resources/ppocr", "*.png");

        foreach (var item in filenames)
        {
            using var mat = Cv2.ImRead(item);
            var rets = engine.DetectAndRecognize(mat);

            foreach (var rec in rets.Select(x => x.RecResult))
            {
                Console.WriteLine(rec);
            }
            PaddleOcrEngine.DrawBoxes(mat, rets.Select(x => x.DetResult).ToArray());
            Cv2.ImShow(item, mat);
        }
        Cv2.WaitKey();
    }
}
