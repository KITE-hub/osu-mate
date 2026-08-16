namespace OsuMate.Services
{
  internal sealed class RelativeWindowSize
  {
    public double Width { get; private set; }
    public double Height { get; private set; }

    public RelativeWindowSize(double width, double height)
    {
      Width = width;
      Height = height;
    }

    public void SetValue(double width, double height)
    {
      Width = width;
      Height = height;
    }
  }
}
