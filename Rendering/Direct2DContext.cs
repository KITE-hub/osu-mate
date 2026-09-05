using System;
using System.IO;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;

namespace OsuMate.Rendering;

internal sealed class Direct2DContext : IDisposable
{
  private ID3D11Device _d3d11Device = null!;
  private ID3D11DeviceContext _d3d11Context = null!;
  private ID2D1Device _d2dDevice = null!;
  private ID2D1DeviceContext _d2dContext = null!;
  private IDWriteFactory _dwriteFactory = null!;
  private IDCompositionDevice _dcompDevice = null!;
  private IDCompositionTarget _dcompTarget = null!;
  private IDCompositionVisual _dcompVisual = null!;
  private IDXGISwapChain1 _swapChain = null!;
  private ID2D1Bitmap1? _targetBitmap;
  private bool _disposed;

  public ID2D1DeviceContext DeviceContext => _d2dContext;
  public IDWriteFactory DWriteFactory => _dwriteFactory;

  public Direct2DContext(IntPtr hwnd, int pixelWidth, int pixelHeight, double dpi)
  {
    var hr = D3D11.D3D11CreateDevice(
      null,
      DriverType.Hardware,
      DeviceCreationFlags.BgraSupport,
      new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 },
      out _d3d11Device,
      out _d3d11Context
    );

    if (hr.Failure)
    {
      D3D11.D3D11CreateDevice(
        null,
        DriverType.Warp,
        DeviceCreationFlags.BgraSupport,
        new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 },
        out _d3d11Device,
        out _d3d11Context
      );
    }

    using var dxgiDevice = _d3d11Device.QueryInterface<IDXGIDevice>();

    using var d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
    _d2dDevice = d2dFactory.CreateDevice(dxgiDevice);
    _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
    _d2dContext.SetDpi((float)dpi, (float)dpi);

    _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();

    _dcompDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
    _dcompDevice.CreateTargetForHwnd(hwnd, true, out _dcompTarget);
    _dcompVisual = _dcompDevice.CreateVisual();
    _dcompTarget.SetRoot(_dcompVisual);

    using var dxgiAdapter = dxgiDevice.GetAdapter();
    using var dxgiFactory = dxgiAdapter.GetParent<IDXGIFactory2>();

    var swapChainDesc = new SwapChainDescription1
    {
      Width = (uint)Math.Max(1, pixelWidth),
      Height = (uint)Math.Max(1, pixelHeight),
      Format = Format.B8G8R8A8_UNorm,
      Stereo = false,
      SampleDescription = new SampleDescription(1, 0),
      BufferUsage = Usage.RenderTargetOutput,
      BufferCount = 2,
      Scaling = Scaling.Stretch,
      SwapEffect = SwapEffect.FlipSequential,
      AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
      Flags = SwapChainFlags.None
    };

    _swapChain = dxgiFactory.CreateSwapChainForComposition(_d3d11Device, swapChainDesc);
    _dcompVisual.SetContent(_swapChain);
    _dcompDevice.Commit();

    CreateTargetBitmap(dpi);
  }

  public void Resize(int pixelWidth, int pixelHeight, double dpi)
  {
    if (_disposed || _swapChain == null || _d2dContext == null)
      return;

    _d2dContext.Target = null;
    _targetBitmap?.Dispose();
    _targetBitmap = null;

    _swapChain.ResizeBuffers(2, (uint)Math.Max(1, pixelWidth), (uint)Math.Max(1, pixelHeight), Format.B8G8R8A8_UNorm, SwapChainFlags.None);
    _d2dContext.SetDpi((float)dpi, (float)dpi);
    CreateTargetBitmap(dpi);
  }

  public void Present()
  {
    if (_disposed || _swapChain == null)
      return;

    _swapChain.Present(1, PresentFlags.None);
  }

  public IDWriteTextFormat CreateKeyTextFormat(float fontSize) => CreateKeyTextFormat(null, fontSize);

  public IDWriteTextFormat CreateKeyTextFormat(string? fontFamilyName, float fontSize)
  {
    var family = string.IsNullOrWhiteSpace(fontFamilyName) ? "Oxanium" : fontFamilyName.Trim();
    try
    {
      var fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Fonts", family, $"{family}-SemiBold.ttf");
      if (!File.Exists(fontPath))
        fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Fonts", family, $"{family}-Regular.ttf");

      if (File.Exists(fontPath))
      {
        var factory5 = _dwriteFactory.QueryInterface<IDWriteFactory5>();
        if (factory5 != null)
        {
          using (factory5)
          {
            using var fontFile = _dwriteFactory.CreateFontFileReference(fontPath);
            using var fontSetBuilder = factory5.CreateFontSetBuilder();
            fontSetBuilder.AddFontFile(fontFile);
            using var fontSet = fontSetBuilder.CreateFontSet();
            using var fontCollection = factory5.CreateFontCollectionFromFontSet(fontSet);
            var format = _dwriteFactory.CreateTextFormat(family, fontCollection, FontWeight.SemiBold, FontStyle.Normal, FontStretch.Normal, fontSize, "en-us");
            format.TextAlignment = TextAlignment.Center;
            format.ParagraphAlignment = ParagraphAlignment.Center;
            return format;
          }
        }
      }

      var systemFormat = _dwriteFactory.CreateTextFormat(family, null, FontWeight.SemiBold, FontStyle.Normal, FontStretch.Normal, fontSize, "en-us");
      systemFormat.TextAlignment = TextAlignment.Center;
      systemFormat.ParagraphAlignment = ParagraphAlignment.Center;
      return systemFormat;
    }
    catch
    {
    }

    var fallback = _dwriteFactory.CreateTextFormat("Segoe UI", null, FontWeight.SemiBold, FontStyle.Normal, FontStretch.Normal, fontSize, "en-us");
    fallback.TextAlignment = TextAlignment.Center;
    fallback.ParagraphAlignment = ParagraphAlignment.Center;
    return fallback;
  }

  private void CreateTargetBitmap(double dpi)
  {
    using var dxgiBackBuffer = _swapChain.GetBuffer<IDXGISurface>(0);
    var bitmapProperties = new BitmapProperties1(
      new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
      (float)dpi,
      (float)dpi,
      BitmapOptions.Target | BitmapOptions.CannotDraw
    );

    _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(dxgiBackBuffer, bitmapProperties);
    _d2dContext.Target = _targetBitmap;
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;

    if (_d2dContext != null)
      _d2dContext.Target = null;

    _targetBitmap?.Dispose();
    _targetBitmap = null;

    _swapChain?.Dispose();
    _dcompVisual?.Dispose();
    _dcompTarget?.Dispose();
    _dcompDevice?.Dispose();
    _dwriteFactory?.Dispose();
    _d2dContext?.Dispose();
    _d2dDevice?.Dispose();
    _d3d11Context?.Dispose();
    _d3d11Device?.Dispose();
  }
}
