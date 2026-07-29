using System.Text;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Protocol;

namespace IwashiScope.Tests;

public sealed class ProtocolTests
{
    private const string Hello =
        """{"protocolVersion":3,"event":"hello","implementation":"IwashiScope spot reader","implementationVersion":1,"argyllVersion":"3.5.0"}""";

    [Fact]
    public void HelloMustBeFirstAndExact()
    {
        var parser = new SpotreadOutputParser(MeasurementMode.Reflectance);
        var beforeHello = parser.Consume(
            """{"protocolVersion":3,"event":"state","state":"ready"}""" + "\n");
        var fatal = Assert.IsType<FatalIssueEvent>(Assert.Single(beforeHello));
        Assert.Equal("missingHello", fatal.Issue.Code);

        var parser2 = new SpotreadOutputParser(MeasurementMode.Reflectance);
        var accepted = parser2.Consume(Hello + "\n");
        Assert.IsType<HelloAcceptedEvent>(Assert.Single(accepted));
        var duplicate = parser2.Consume(Hello + "\n");
        Assert.Equal("duplicateHello", Assert.IsType<FatalIssueEvent>(Assert.Single(duplicate)).Issue.Code);
    }

    [Fact]
    public void Utf8ChunksMaySplitAtEveryByte()
    {
        var parser = new SpotreadOutputParser(MeasurementMode.Reflectance);
        var text = Hello + "\r\n" +
            """{"protocolVersion":3,"event":"instrument","name":"測色計","serialNumber":"日本語"}""" +
            "\r\n" +
            """{"protocolVersion":3,"event":"state","state":"ready"}""";
        var events = new List<SpotreadEvent>();
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            events.AddRange(parser.Consume(new[] { value }));
        }
        events.AddRange(parser.Finish());

        Assert.Collection(
            events,
            @event => Assert.IsType<HelloAcceptedEvent>(@event),
            @event =>
            {
                var identity = Assert.IsType<InstrumentIdentityEvent>(@event);
                Assert.Equal("測色計", identity.Identity.Name);
                Assert.Equal("日本語", identity.Identity.SerialNumber);
            },
            @event => Assert.IsType<MeasurementPromptEvent>(@event));
    }

    [Fact]
    public void ReflectanceMeasurementParsesPracticalRangeAndVectors()
    {
        var parser = new SpotreadOutputParser(
            MeasurementMode.Reflectance,
            () => DateTimeOffset.UnixEpoch);
        var json = Hello + "\n" +
            """
            {"protocolVersion":3,"event":"measurement","mode":"reflectance","spectrum":{"startNm":380,"endNm":720,"practicalStartNm":400,"practicalEndNm":700,"values":[0.1,0.2,0.4,0.3]},"xyz":[41.2,44.8,28.1],"lab":[72.8,-18.4,32.7],"labWhitePoint":"D50","lightingMetricIssues":[]}
            """;
        var events = parser.Consume(json + "\n");
        Assert.IsType<HelloAcceptedEvent>(events[0]);
        var measurement = Assert.IsType<MeasurementCompletedEvent>(events[1]).Measurement;

        Assert.Equal(DateTimeOffset.UnixEpoch, measurement.CapturedAt);
        Assert.Equal(new WavelengthRange(400, 700), measurement.PracticalSpectrumRange);
        Assert.Equal(4, measurement.Spectrum.Count);
        Assert.Equal(380, measurement.Spectrum[0].Wavelength);
        Assert.Equal(720, measurement.Spectrum[^1].Wavelength);
        Assert.Equal(new Vector3(41.2, 44.8, 28.1), measurement.Xyz);
    }

    [Fact]
    public void WrongMeasurementModeBecomesConfigurationIssue()
    {
        var parser = new SpotreadOutputParser(MeasurementMode.Ambient);
        var json = Hello + "\n" +
            """
            {"protocolVersion":3,"event":"measurement","mode":"emissive","spectrum":{"startNm":400,"endNm":700,"values":[]},"xyz":[1,1,1],"lab":[1,1,1]}
            """;
        var events = parser.Consume(json + "\n");
        Assert.IsType<ConfigurationIssueEvent>(events[1]);
    }

    [Fact]
    public void InvalidUtf8IsRejected()
    {
        var stream = new Utf8JsonLineStream();
        Assert.Throws<DecoderFallbackException>(() => stream.Consume([0xC3, 0x28]));
    }
}
