using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpDX;
using SharpDX.IO;
// use namespaces shortcuts to reduce typing and avoid the messing the same class names from different namespaces
using d2 = SharpDX.Direct2D1;
using d3d = SharpDX.Direct3D11;
using dxgi = SharpDX.DXGI;
using wic = SharpDX.WIC;
using dw = SharpDX.DirectWrite;
using System.Diagnostics;
using System;
using System.IO;

namespace GdiBench
{
    public class Direct2D
    {
        public static MemoryStream Resize(System.IO.Stream source, int maxwidth, int maxheight, Action beforeDrawImage, Action afterDrawImage)
        {
            // initialize the D3D device which will allow to render to image any graphics - 3D or 2D
            var defaultDevice = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Warp,
                                                              d3d.DeviceCreationFlags.BgraSupport | d3d.DeviceCreationFlags.SingleThreaded | d3d.DeviceCreationFlags.PreventThreadingOptimizations); 


            var d3dDevice = defaultDevice.QueryInterface<d3d.Device1>(); // get a reference to the Direct3D 11.1 device
            var dxgiDevice = d3dDevice.QueryInterface<dxgi.Device>(); // get a reference to DXGI device

            var d2dDevice = new d2.Device(dxgiDevice); // initialize the D2D device

            var imagingFactory = new wic.ImagingFactory2(); // initialize the WIC factory

            // initialize the DeviceContext - it will be the D2D render target and will allow all rendering operations
            var d2dContext = new d2.DeviceContext(d2dDevice, d2.DeviceContextOptions.None);

            var dwFactory = new dw.Factory();

            // specify a pixel format that is supported by both D2D and WIC
            var d2PixelFormat = new d2.PixelFormat(dxgi.Format.R8G8B8A8_UNorm, d2.AlphaMode.Premultiplied);
            // if in D2D was specified an R-G-B-A format - use the same for wic
            var wicPixelFormat = wic.PixelFormat.Format32bppPRGBA;

            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            // IMAGE LOADING ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            var decoder = new wic.BitmapDecoder(imagingFactory,source, wic.DecodeOptions.CacheOnLoad);
        
            // decode the loaded image to a format that can be consumed by D2D
            var formatConverter = new wic.FormatConverter(imagingFactory);
            formatConverter.Initialize(decoder.GetFrame(0), wicPixelFormat);

            // store the image size - output will be of the same size
            var inputImageSize = formatConverter.Size;

            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            // RENDER TARGET SETUP ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            // create the d2d bitmap description using default flags (from SharpDX samples) and 96 DPI
            var d2dBitmapProps = new d2.BitmapProperties1(d2PixelFormat, 96, 96, d2.BitmapOptions.Target | d2.BitmapOptions.CannotDraw);


            //Calculate size
            var resultSize = MathUtil.ScaleWithin(inputImageSize.Width,inputImageSize.Height,maxwidth,maxheight);
            var newWidth = resultSize.Item1;
            var newHeight = resultSize.Item2;

            // the render target
            var d2dRenderTarget = new d2.Bitmap1(d2dContext, new Size2(newWidth, newHeight), d2dBitmapProps);
            d2dContext.Target = d2dRenderTarget; // associate bitmap with the d2d context

            var bitmapSourceEffect = new d2.Effects.BitmapSourceEffect(d2dContext);
            bitmapSourceEffect.WicBitmapSource = formatConverter;


            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            // DRAWING ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            beforeDrawImage();
            // slow preparations - fast drawing:
            d2dContext.BeginDraw();
            d2dContext.Transform = Matrix3x2.Scaling(new Vector2((float)(newWidth / (float)inputImageSize.Width), (float)(newHeight / (float)inputImageSize.Height)));
            d2dContext.DrawImage(bitmapSourceEffect, d2.InterpolationMode.HighQualityCubic);
            d2dContext.EndDraw();
            afterDrawImage();

            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            // IMAGE SAVING ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            var ms = new MemoryStream();

            // use the appropiate overload to write either to stream or to a file
            var stream = new wic.WICStream(imagingFactory,ms);

            // select the image encoding format HERE
            var encoder = new wic.JpegBitmapEncoder(imagingFactory);
            encoder.Initialize(stream);

            var bitmapFrameEncode = new wic.BitmapFrameEncode(encoder);
            bitmapFrameEncode.Initialize();
            bitmapFrameEncode.SetSize(newWidth, newHeight);
            bitmapFrameEncode.SetPixelFormat(ref wicPixelFormat);

            // this is the trick to write D2D1 bitmap to WIC
            var imageEncoder = new wic.ImageEncoder(imagingFactory, d2dDevice);
            imageEncoder.WriteFrame(d2dRenderTarget, bitmapFrameEncode, new wic.ImageParameters(d2PixelFormat, 96, 96,  0, 0, newWidth, newHeight));

            bitmapFrameEncode.Commit();
            encoder.Commit();


            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            // CLEANUP ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            // dispose everything and free used resources

            bitmapFrameEncode.Dispose();
            encoder.Dispose();
            stream.Dispose();
            formatConverter.Dispose();
            bitmapSourceEffect.Dispose();
            d2dRenderTarget.Dispose();
            decoder.Dispose();
            d2dContext.Dispose();
            dwFactory.Dispose();
            imagingFactory.Dispose();
            d2dDevice.Dispose();
            dxgiDevice.Dispose();
            d3dDevice.Dispose();
            defaultDevice.Dispose();
            return ms;
        }
    }
}
