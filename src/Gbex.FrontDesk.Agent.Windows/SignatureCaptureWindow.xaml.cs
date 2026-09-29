using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gbex.FrontDesk.Agent.Windows;

public partial class SignatureCaptureWindow : Window
{
    public string? CapturedFilePath { get; private set; }

    public SignatureCaptureWindow()
    {
        InitializeComponent();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SignatureCanvas.Strokes.Clear();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SignatureCanvas.Strokes.Count == 0)
        {
            MessageBox.Show(this, "Önce müşteri imzasını alın.", "GBEX İmza", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SignatureCanvas.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(SignatureCanvas.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(SignatureCanvas.ActualHeight));
        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var brush = new VisualBrush(SignatureCanvas);
            context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        renderTarget.Render(visual);

        var target = Path.Combine(
            Path.GetTempPath(),
            $"gbex-signature-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.png"
        );

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using (var stream = File.Create(target))
        {
            encoder.Save(stream);
        }

        CapturedFilePath = target;
        DialogResult = true;
    }
}
