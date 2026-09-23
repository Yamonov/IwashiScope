using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IwashiScope.App.Wpf.Controls;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class ApprovedChromaticityTests
{
    private sealed class LocalInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mac-build66-golden.json")));
    private static Vector3 V(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
    private static double[] C(Vector3 v) => [v.First, v.Second, v.Third];
    private static void Close(double a, double b, double tolerance = 1e-9) => Assert.True(double.IsFinite(b) && Math.Abs(a-b) <= tolerance, $"Expected {a:R}, actual {b:R}, delta {b-a:R}");
    private static Vector3 Scale(Vector3 v, double s) => new(v.First*s,v.Second*s,v.Third*s);
    private static Vector3 Add(Vector3 a, Vector3 b) => new(a.First+b.First,a.Second+b.Second,a.Third+b.Third);
    private static double Dot(Vector3 a, Vector3 b) => a.First*b.First+a.Second*b.Second+a.Third*b.Third;
    private static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Second*b.Third-a.Third*b.Second,a.Third*b.First-a.First*b.Third,a.First*b.Second-a.Second*b.First);
    private static Vector3 Solve(Vector3 a, Vector3 b, Vector3 c, Vector3 value)
    {
        var determinant = Dot(a,Cross(b,c));
        return new(Dot(value,Cross(b,c))/determinant,Dot(a,Cross(value,c))/determinant,Dot(a,Cross(b,value))/determinant);
    }
    [Fact]
    public void DenseBackgroundMatchesApprovedNativeNamedSpaceFixtures()
    {
        using var document=Golden();
        foreach (var background in document.RootElement.GetProperty("backgrounds").EnumerateArray())
        {
            var space=background.GetProperty("profile").GetString()=="sRGB" ? ChromaticityRenderingSpace.Srgb : ChromaticityRenderingSpace.DisplayP3;
            var model=new ChromaticityIllustrationBackground(space, MacColorTransformReference.For(background.GetProperty("profile").GetString()!));
            foreach(var sample in background.GetProperty("samples").EnumerateArray())
            {
                var point=sample.GetProperty("xyF"); var actual=model.ColorAt(new(point[0].GetDouble(),point[1].GetDouble()));
                Assert.NotNull(actual);
                var expected=V(sample.GetProperty("linearRGB"));
                for(var i=0;i<3;i++) {Close(C(expected)[i],C(actual)[i],space == ChromaticityRenderingSpace.Srgb ? 1e-9 : 2e-5);Assert.InRange(C(actual)[i],0,1);}
                Close(0.2,model.IllustrativeLuminance(actual),1e-10);
            }
        }
        Assert.Equal(0.38,ChromaticityIllustrationBackground.Opacity);
    }
    [Fact]
    public void NamedMacProfileProbesDocumentTheDifferenceFromIdealP3Primaries()
    {
        using var probes = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","mac-named-space-transforms.json")));
        foreach(var profile in probes.RootElement.EnumerateArray())
        foreach(var probe in profile.GetProperty("probes").EnumerateArray())
        {
            var source=V(probe.GetProperty("inputLinearSRGB"));var expected=V(probe.GetProperty("outputLinearRGB"));
            var actual=MacColorTransformReference.For(profile.GetProperty("name").GetString()!)(source);
            for(var i=0;i<3;i++)Close(C(expected)[i],C(actual)[i],2e-7);
        }
        var ideal=ChromaticityDisplayColorTransform.FromSrgb(new(1,0,0),ChromaticityRenderingSpace.DisplayP3);
        var native=MacColorTransformReference.For("Display P3")(new(1,0,0));
        Assert.InRange(Math.Abs(ideal.First-native.First),3e-5,4e-5);
    }
    [Fact]
    public void BackgroundIsSmoothAcrossBlueMagentaAndOldMLimit()
    {
        var renderer=new ChromaticityIllustrationBackground(); double previous=double.PositiveInfinity;
        for(var i=0;i<=1024;i++)
        {
            var x=0.16+0.26*i/1024;var rgb=renderer.ColorAt(new(x,0.18))!;
            Assert.True(rgb.Second<=previous+1e-10);Assert.True(C(rgb).Max()-C(rgb).Min()>0.25);previous=rgb.Second;
        }
        var white=ChromaticityBackgroundPalette.Shared.White.XyzF;
        var sw=new Vector3(white.First/white.Second,1,white.Third/white.Second);
        var d65=new Vector3(.3127/.3290,1,(1-.3127-.3290)/.3290);
        var point=new PhysiologicalPoint(.2389,.2299);var pole=Cie2006Chromaticity.CopunctalPoint(PhysiologicalCone.Medium);
        PhysiologicalPoint At(double x)=>new(x,point.Y+(x-point.X)*(pole.Y-point.Y)/(pole.X-point.X));
        double RawRed(double x) {var p=At(x);return .2*ChromaticityDisplayColorTransform.XyzToSrgb(BradfordChromaticAdaptation.Adapt(new(p.X/p.Y,1,(1-p.X-p.Y)/p.Y),sw,d65)!).First;}
        double lower=.05,upper=.35;
        Assert.True(RawRed(lower)<0);Assert.True(RawRed(upper)>0);
        for(var i=0;i<48;i++){var mid=(lower+upper)/2;if(RawRed(mid)<0)lower=mid;else upper=mid;}
        var center=(lower+upper)/2; const double step=1e-6;
        var a=C(renderer.ColorAt(At(center-step))!);var b=C(renderer.ColorAt(At(center))!);var c=C(renderer.ColorAt(At(center+step))!);
        for(var i=0;i<3;i++) Assert.True(Math.Abs((c[i]-b[i])/step-(b[i]-a[i])/step)<.001);
    }
    [Fact]
    public void BackgroundFloatRasterHasNoTransparentSeamsAndRejectsInvalidInput()
    {
        var model=new ChromaticityIllustrationBackground();var pixels=model.Raster(160,180)!;
        Assert.Equal(160*180*4,pixels.Length);
        Assert.All(pixels,v=>Assert.InRange(v,0,1));
        for(var i=3;i<pixels.Length;i+=4)Assert.Equal(1f,pixels[i]);
        Assert.Null(model.Raster(0,1));Assert.Null(model.Raster(4097,1));
        Assert.Null(model.ColorAt(new(double.NaN,.2)));Assert.Null(model.ColorAt(new(.2,0)));
    }
    [Theory]
    [InlineData(390,730)] [InlineData(421,702)] [InlineData(500,600)] [InlineData(616,730)]
    public void MeasuredRangeWhiteRetainsNeutralRatios(double lower,double upper)
    {
        foreach(var value in new[]{25d,50,150})
        {
            var m=new SpotMeasurement {Mode=MeasurementMode.Reflectance,SpectrumStart=lower,SpectrumEnd=upper,DeclaredStepCount=2,Spectrum=[new(0,lower,value),new(1,upper,value)]};
            var result=Cie2006Chromaticity.Measure(m,out _);Assert.NotNull(result);
            Close(100,Cie2006Chromaticity.XyzF(result.WhiteLms).Second);
            foreach(var mode in new[]{ConfusionColorMode.P,ConfusionColorMode.D,ConfusionColorMode.T})
            {
                var reference=ChromaticityBrightnessReference.Create(result.Lms,result.WhiteLms,mode);Assert.NotNull(reference);
                Close(value/100,reference.RequestedLuminance);Close(Math.Min(1,value/100),reference.RelativeLuminance);
                Assert.Equal(value>100,reference.IsDisplayLimited);
            }
        }
    }
    [Fact]
    public void QIsIndependentlyRecoveredFromProducedRgbAndRawSignalsRemainMatched()
    {
        using var document=Golden();
        foreach(var fixture in document.RootElement.GetProperty("seamFixtures").EnumerateArray())
        foreach(var mode in new[]{ConfusionColorMode.P,ConfusionColorMode.D,ConfusionColorMode.T})
        foreach(var space in Enum.GetValues<ChromaticityRenderingSpace>())
        {
            var lms=V(fixture.GetProperty("lms"));var white=V(fixture.GetProperty("whiteLMS"));
            var reference=ChromaticityBrightnessReference.Create(lms,white,mode)!;
            var band=ChromaticityConfusionBand.Make(lms,mode)!;
            Assert.NotEmpty(band.Sections);Close(0,band.Sections[0].Start);Close(1,band.Sections[^1].End);
            var mapper=new ChromaticityBandDisplayMapper(reference,space);
            foreach(var section in band.Sections)
            for(var i=1;i<32;i++)
            {
                var stop=section.SampleAt(i/32d)!;var mapped=mapper.Sample(stop)!;
                var triangle=ChromaticityBackgroundPalette.Shared.Triangles.First(t=>t.Weights(stop.Point)!=null);
                var a=ChromaticityDisplayColorTransform.FromSrgb(triangle.A.LinearRgb,space);
                var b=ChromaticityDisplayColorTransform.FromSrgb(triangle.B.LinearRgb,space);
                var c=ChromaticityDisplayColorTransform.FromSrgb(triangle.C.LinearRgb,space);
                var weights=Solve(a,b,c,mapped.LinearRgb);
                var xyz=triangle.Mix(v=>v.XyzF,weights);
                var recovered=Scale(ChromaticityConfusionBand.LmsFromXyzF(xyz),100/ChromaticityBackgroundPalette.Shared.White.XyzF.Second);
                // Interior recovery is independent of the mapper's ModelLms metadata.
                Close(reference.RelativeLuminance,ChromaticityBrightnessReference.Response(recovered,ChromaticityBrightnessReference.PaletteWhiteLms,mode)!.Value,1e-7);
                var indices=mode==ConfusionColorMode.P?new[]{1,2}:mode==ConfusionColorMode.D?new[]{0,2}:new[]{0,1};
                foreach(var index in indices)Close(C(lms)[index],C(stop.Lms)[index],1e-8);
            }
        }
    }
    [Fact]
    public void InvalidReferencesNeverSilentlyUseLabOrAnotherType()
    {
        var white=ChromaticityBrightnessReference.PaletteWhiteLms;
        Assert.Null(ChromaticityBrightnessReference.Create(white,white,ConfusionColorMode.C));
        Assert.Null(ChromaticityBrightnessReference.Create(new(-1,1,1),white,ConfusionColorMode.P));
        Assert.Null(ChromaticityBrightnessReference.Create(white,new(0,0,0),ConfusionColorMode.T));
        Assert.Null(ChromaticityConfusionBand.Make(new(0,0,0),ConfusionColorMode.P));
        var black=ChromaticityBrightnessReference.Create(new(0,0,0),white,ConfusionColorMode.P)!;
        Close(0,black.RelativeLuminance);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach(var nested in Descendants(child))yield return nested;
    }
    [Fact]
    public void NewControlsFollowLanguageAndLmsNotLabAndFitNarrowWidth()
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                var m=new SpotMeasurement {Mode=MeasurementMode.Reflectance,SpectrumStart=390,SpectrumEnd=730,DeclaredStepCount=2,Spectrum=[new(0,390,50),new(1,730,50)],Lab=null};
                var view=new ReflectanceChromaticityView {Measurement=m,Width=320};
                var buttons=Descendants(view).OfType<RadioButton>().ToArray();
                var reference=Descendants(view).OfType<ChromaticityReferenceButton>().Single();
                Assert.True(buttons[0].IsChecked);Assert.False(reference.IsEnabled);Assert.All(buttons,b=>Assert.True(b.IsEnabled));
                buttons[1].IsChecked=true;Assert.True(reference.IsEnabled);
                var source = new LocalInputSource { RootVisual = view };
                void Press() => reference.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                void Release() => reference.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
                Press(); Assert.True(reference.IsReferencePressed); Release(); Assert.False(reference.IsReferencePressed);
                Press(); reference.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent }); Assert.False(reference.IsReferencePressed);
                Press(); reference.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); Assert.False(reference.IsReferencePressed);
                Press(); reference.IsEnabled = false; Assert.False(reference.IsReferencePressed); Press(); Assert.False(reference.IsReferencePressed); reference.IsEnabled = true;
                Press(); view.Measurement = m with { Lab = new(40, 0, 0) }; Assert.False(reference.IsReferencePressed);
                Press(); buttons[2].IsChecked = true; Assert.False(reference.IsReferencePressed);
                Press(); view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Assert.False(reference.IsReferencePressed);
                Press(); typeof(ReflectanceChromaticityView).GetMethod("HostDeactivated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, new object?[] { null, EventArgs.Empty }); Assert.False(reference.IsReferencePressed);
                view.UiLanguage="en";
                Assert.Equal("Brightness (ref.)",reference.Content);
                Assert.DoesNotContain(Descendants(view).OfType<TextBlock>(),t=>t.Text.Contains("測定")||t.Text.Contains("色覚")||t.Text.Contains("混同"));
                view.UiLanguage="ja";Assert.Equal("明るさ（参考）",reference.Content);
                view.Measure(new Size(320,double.PositiveInfinity));view.Arrange(new Rect(new Point(),view.DesiredSize));view.UpdateLayout();
                Assert.True(reference.ActualWidth>0);
                var p=reference.TranslatePoint(new Point(reference.ActualWidth,0),view);Assert.InRange(p.X,0,320);
                buttons[0].IsChecked=true;Assert.False(reference.IsEnabled);Assert.False(reference.IsReferencePressed);
                view.Measurement=m with {Spectrum=[]};Assert.All(buttons,b=>Assert.False(b.IsEnabled));
            }catch(Exception ex){failure=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)),"Isolated control test timed out.");
        if(failure!=null)throw new InvalidOperationException("Control test failed.",failure);
    }
}
