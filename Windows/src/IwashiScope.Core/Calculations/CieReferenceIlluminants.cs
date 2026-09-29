/*
 SPDX-FileCopyrightText: 2026 Yamonov
 SPDX-License-Identifier: AGPL-3.0-only
*/
using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public enum CieIlluminantCategory
{
    StandardAndDaylight,
    IndoorDaylight,
    FluorescentFL,
    FluorescentFL3,
    HighPressureDischarge,
    Led,
    Calibration,
}

public enum CieReferenceIlluminant
{
    A,
    C,
    D50,
    D55,
    D65,
    D75,
    ID50,
    ID65,
    FL1,
    FL2,
    FL3,
    FL4,
    FL5,
    FL6,
    FL7,
    FL8,
    FL9,
    FL10,
    FL11,
    FL12,
    FL3_1,
    FL3_2,
    FL3_3,
    FL3_4,
    FL3_5,
    FL3_6,
    FL3_7,
    FL3_8,
    FL3_9,
    FL3_10,
    FL3_11,
    FL3_12,
    FL3_13,
    FL3_14,
    FL3_15,
    HP1,
    HP2,
    HP3,
    HP4,
    HP5,
    LEDB1,
    LEDB2,
    LEDB3,
    LEDB4,
    LEDB5,
    LEDBH1,
    LEDRGB1,
    LEDV1,
    LEDV2,
    L41,
}

public static class CieReferenceIlluminants
{
    public const double StartWavelength = 380;
    public const double EndWavelength = 730;
    public const double Interval = 5;

    public static IReadOnlyList<SpectralSample> Samples(CieReferenceIlluminant illuminant) =>
        CieReferenceIlluminantData.ValuesByIlluminant[illuminant]
            .Select((value, index) =>
                new SpectralSample(index, StartWavelength + index * Interval, value))
            .ToArray();

    public static CieIlluminantCategory Category(CieReferenceIlluminant illuminant) => illuminant switch
    {
        CieReferenceIlluminant.A or CieReferenceIlluminant.C or
            CieReferenceIlluminant.D50 or CieReferenceIlluminant.D55 or
            CieReferenceIlluminant.D65 or CieReferenceIlluminant.D75 =>
            CieIlluminantCategory.StandardAndDaylight,
        CieReferenceIlluminant.ID50 or CieReferenceIlluminant.ID65 =>
            CieIlluminantCategory.IndoorDaylight,
        CieReferenceIlluminant.FL1 or CieReferenceIlluminant.FL2 or
            CieReferenceIlluminant.FL3 or CieReferenceIlluminant.FL4 or
            CieReferenceIlluminant.FL5 or CieReferenceIlluminant.FL6 or
            CieReferenceIlluminant.FL7 or CieReferenceIlluminant.FL8 or
            CieReferenceIlluminant.FL9 or CieReferenceIlluminant.FL10 or
            CieReferenceIlluminant.FL11 or CieReferenceIlluminant.FL12 =>
            CieIlluminantCategory.FluorescentFL,
        CieReferenceIlluminant.FL3_1 or CieReferenceIlluminant.FL3_2 or
            CieReferenceIlluminant.FL3_3 or CieReferenceIlluminant.FL3_4 or
            CieReferenceIlluminant.FL3_5 or CieReferenceIlluminant.FL3_6 or
            CieReferenceIlluminant.FL3_7 or CieReferenceIlluminant.FL3_8 or
            CieReferenceIlluminant.FL3_9 or CieReferenceIlluminant.FL3_10 or
            CieReferenceIlluminant.FL3_11 or CieReferenceIlluminant.FL3_12 or
            CieReferenceIlluminant.FL3_13 or CieReferenceIlluminant.FL3_14 or
            CieReferenceIlluminant.FL3_15 =>
            CieIlluminantCategory.FluorescentFL3,
        CieReferenceIlluminant.HP1 or CieReferenceIlluminant.HP2 or
            CieReferenceIlluminant.HP3 or CieReferenceIlluminant.HP4 or
            CieReferenceIlluminant.HP5 =>
            CieIlluminantCategory.HighPressureDischarge,
        CieReferenceIlluminant.LEDB1 or CieReferenceIlluminant.LEDB2 or
            CieReferenceIlluminant.LEDB3 or CieReferenceIlluminant.LEDB4 or
            CieReferenceIlluminant.LEDB5 or CieReferenceIlluminant.LEDBH1 or
            CieReferenceIlluminant.LEDRGB1 or CieReferenceIlluminant.LEDV1 or
            CieReferenceIlluminant.LEDV2 =>
            CieIlluminantCategory.Led,
        _ => CieIlluminantCategory.Calibration,
    };

    public static string RawValue(CieReferenceIlluminant illuminant) => illuminant switch
    {
        CieReferenceIlluminant.FL3_1 => "FL3.1",
        CieReferenceIlluminant.FL3_2 => "FL3.2",
        CieReferenceIlluminant.FL3_3 => "FL3.3",
        CieReferenceIlluminant.FL3_4 => "FL3.4",
        CieReferenceIlluminant.FL3_5 => "FL3.5",
        CieReferenceIlluminant.FL3_6 => "FL3.6",
        CieReferenceIlluminant.FL3_7 => "FL3.7",
        CieReferenceIlluminant.FL3_8 => "FL3.8",
        CieReferenceIlluminant.FL3_9 => "FL3.9",
        CieReferenceIlluminant.FL3_10 => "FL3.10",
        CieReferenceIlluminant.FL3_11 => "FL3.11",
        CieReferenceIlluminant.FL3_12 => "FL3.12",
        CieReferenceIlluminant.FL3_13 => "FL3.13",
        CieReferenceIlluminant.FL3_14 => "FL3.14",
        CieReferenceIlluminant.FL3_15 => "FL3.15",
        CieReferenceIlluminant.LEDB1 => "LED-B1",
        CieReferenceIlluminant.LEDB2 => "LED-B2",
        CieReferenceIlluminant.LEDB3 => "LED-B3",
        CieReferenceIlluminant.LEDB4 => "LED-B4",
        CieReferenceIlluminant.LEDB5 => "LED-B5",
        CieReferenceIlluminant.LEDBH1 => "LED-BH1",
        CieReferenceIlluminant.LEDRGB1 => "LED-RGB1",
        CieReferenceIlluminant.LEDV1 => "LED-V1",
        CieReferenceIlluminant.LEDV2 => "LED-V2",
        _ => illuminant.ToString(),
    };

    public static string DisplayName(CieReferenceIlluminant illuminant, bool japanese) => illuminant switch
    {
        CieReferenceIlluminant.A => japanese ? "A（白熱電球）" : "A (Incandescent)",
        CieReferenceIlluminant.C => japanese ? "C（旧昼光）" : "C (Legacy Daylight)",
        CieReferenceIlluminant.D50 => japanese ? "D50（5000 K 昼光）" : "D50 (5000 K Daylight)",
        CieReferenceIlluminant.D55 => japanese ? "D55（5500 K 昼光）" : "D55 (5500 K Daylight)",
        CieReferenceIlluminant.D65 => japanese ? "D65（6500 K 昼光）" : "D65 (6500 K Daylight)",
        CieReferenceIlluminant.D75 => japanese ? "D75（7500 K 昼光）" : "D75 (7500 K Daylight)",
        CieReferenceIlluminant.ID50 => japanese ? "ID50（屋内昼光）" : "ID50 (Indoor Daylight)",
        CieReferenceIlluminant.ID65 => japanese ? "ID65（屋内昼光）" : "ID65 (Indoor Daylight)",
        CieReferenceIlluminant.L41 => japanese ? "L41（測光器校正用）" : "L41 (Photometer Calibration)",
        _ => RawValue(illuminant),
    };

    public static string MenuDisplayName(CieReferenceIlluminant illuminant, bool japanese)
    {
        var (ja, en) = illuminant switch
        {
            CieReferenceIlluminant.A => ("白熱電球・約2856 K", "Tungsten incandescent · approx. 2856 K"),
            CieReferenceIlluminant.C => ("旧来の昼光・約6800 K", "Legacy daylight · approx. 6800 K"),
            CieReferenceIlluminant.D50 => ("印刷・写真の昼光・約5000 K", "Graphic arts daylight · approx. 5000 K"),
            CieReferenceIlluminant.D55 => ("昼光・約5500 K", "Daylight · approx. 5500 K"),
            CieReferenceIlluminant.D65 => ("代表的な屋外昼光・約6500 K", "Average outdoor daylight · approx. 6500 K"),
            CieReferenceIlluminant.D75 => ("昼光・約7500 K", "Daylight · approx. 7500 K"),
            CieReferenceIlluminant.ID50 => ("屋内昼光・約5000 K", "Indoor daylight · approx. 5000 K"),
            CieReferenceIlluminant.ID65 => ("屋内昼光・約6500 K", "Indoor daylight · approx. 6500 K"),
            CieReferenceIlluminant.FL1 => ("標準型・約6430 K・Ra 76", "Standard fluorescent · approx. 6430 K · Ra 76"),
            CieReferenceIlluminant.FL2 => ("標準型・約4230 K・Ra 64", "Standard fluorescent · approx. 4230 K · Ra 64"),
            CieReferenceIlluminant.FL3 => ("標準型・約3450 K・Ra 57", "Standard fluorescent · approx. 3450 K · Ra 57"),
            CieReferenceIlluminant.FL4 => ("標準型・約2940 K・Ra 51", "Standard fluorescent · approx. 2940 K · Ra 51"),
            CieReferenceIlluminant.FL5 => ("標準型・約6350 K・Ra 72", "Standard fluorescent · approx. 6350 K · Ra 72"),
            CieReferenceIlluminant.FL6 => ("標準型・約4150 K・Ra 59", "Standard fluorescent · approx. 4150 K · Ra 59"),
            CieReferenceIlluminant.FL7 => ("広帯域型・D65模擬・約6500 K", "Broadband D65 simulator · approx. 6500 K"),
            CieReferenceIlluminant.FL8 => ("広帯域型・D50模擬・約5000 K", "Broadband D50 simulator · approx. 5000 K"),
            CieReferenceIlluminant.FL9 => ("広帯域型・約4150 K・Ra 90", "Broadband fluorescent · approx. 4150 K · Ra 90"),
            CieReferenceIlluminant.FL10 => ("狭帯域型・約5000 K・Ra 81", "Narrowband fluorescent · approx. 5000 K · Ra 81"),
            CieReferenceIlluminant.FL11 => ("狭帯域型・約4000 K・Ra 83", "Narrowband fluorescent · approx. 4000 K · Ra 83"),
            CieReferenceIlluminant.FL12 => ("狭帯域型・約3000 K・Ra 83", "Narrowband fluorescent · approx. 3000 K · Ra 83"),
            CieReferenceIlluminant.FL3_1 => ("ハロリン酸塩型・約2932 K・Ra 51", "Halophosphate · approx. 2932 K · Ra 51"),
            CieReferenceIlluminant.FL3_2 => ("ハロリン酸塩型・約3965 K・Ra 70", "Halophosphate · approx. 3965 K · Ra 70"),
            CieReferenceIlluminant.FL3_3 => ("ハロリン酸塩型・約6280 K・Ra 72", "Halophosphate · approx. 6280 K · Ra 72"),
            CieReferenceIlluminant.FL3_4 => ("高演色型・約2904 K・Ra 87", "DeLuxe fluorescent · approx. 2904 K · Ra 87"),
            CieReferenceIlluminant.FL3_5 => ("高演色型・約4086 K・Ra 95", "DeLuxe fluorescent · approx. 4086 K · Ra 95"),
            CieReferenceIlluminant.FL3_6 => ("高演色型・約4894 K・Ra 96", "DeLuxe fluorescent · approx. 4894 K · Ra 96"),
            CieReferenceIlluminant.FL3_7 => ("3波長型・約2979 K・Ra 82", "Three-band fluorescent · approx. 2979 K · Ra 82"),
            CieReferenceIlluminant.FL3_8 => ("3波長型・約4006 K・Ra 79", "Three-band fluorescent · approx. 4006 K · Ra 79"),
            CieReferenceIlluminant.FL3_9 => ("3波長型・約4853 K・Ra 79", "Three-band fluorescent · approx. 4853 K · Ra 79"),
            CieReferenceIlluminant.FL3_10 => ("3波長型・約5000 K・Ra 88", "Three-band fluorescent · approx. 5000 K · Ra 88"),
            CieReferenceIlluminant.FL3_11 => ("3波長型・約5854 K・Ra 78", "Three-band fluorescent · approx. 5854 K · Ra 78"),
            CieReferenceIlluminant.FL3_12 => ("多波長型・約2984 K・Ra 93", "Multi-band fluorescent · approx. 2984 K · Ra 93"),
            CieReferenceIlluminant.FL3_13 => ("多波長型・約3896 K・Ra 96", "Multi-band fluorescent · approx. 3896 K · Ra 96"),
            CieReferenceIlluminant.FL3_14 => ("多波長型・約5045 K・Ra 95", "Multi-band fluorescent · approx. 5045 K · Ra 95"),
            CieReferenceIlluminant.FL3_15 => ("D65模擬・約6509 K・Ra 98", "D65 fluorescent simulator · approx. 6509 K · Ra 98"),
            CieReferenceIlluminant.HP1 => ("標準型高圧ナトリウム・約1959 K・Ra 8", "Standard high-pressure sodium · approx. 1959 K · Ra 8"),
            CieReferenceIlluminant.HP2 => ("演色改善型高圧ナトリウム・約2506 K・Ra 83", "Colour-enhanced high-pressure sodium · approx. 2506 K · Ra 83"),
            CieReferenceIlluminant.HP3 => ("メタルハライド・約3144 K・Ra 83", "Metal halide · approx. 3144 K · Ra 83"),
            CieReferenceIlluminant.HP4 => ("メタルハライド・約4002 K・Ra 74", "Metal halide · approx. 4002 K · Ra 74"),
            CieReferenceIlluminant.HP5 => ("メタルハライド・約4039 K・Ra 87", "Metal halide · approx. 4039 K · Ra 87"),
            CieReferenceIlluminant.LEDB1 => ("青色励起・蛍光体型・約2733 K", "Blue-pumped phosphor LED · approx. 2733 K"),
            CieReferenceIlluminant.LEDB2 => ("青色励起・蛍光体型・約2998 K", "Blue-pumped phosphor LED · approx. 2998 K"),
            CieReferenceIlluminant.LEDB3 => ("青色励起・蛍光体型・約4103 K", "Blue-pumped phosphor LED · approx. 4103 K"),
            CieReferenceIlluminant.LEDB4 => ("青色励起・蛍光体型・約5109 K", "Blue-pumped phosphor LED · approx. 5109 K"),
            CieReferenceIlluminant.LEDB5 => ("青色励起・蛍光体型・約6598 K", "Blue-pumped phosphor LED · approx. 6598 K"),
            CieReferenceIlluminant.LEDBH1 => ("複合型・約2851 K", "Hybrid LED · approx. 2851 K"),
            CieReferenceIlluminant.LEDRGB1 => ("RGB混色型・約2840 K", "RGB LED · approx. 2840 K"),
            CieReferenceIlluminant.LEDV1 => ("紫色励起・蛍光体型・約2724 K", "Violet-pumped phosphor LED · approx. 2724 K"),
            CieReferenceIlluminant.LEDV2 => ("紫色励起・蛍光体型・約4070 K", "Violet-pumped phosphor LED · approx. 4070 K"),
            CieReferenceIlluminant.L41 => ("LED測定向け測光器校正・約4100 K", "Photometer calibration for LEDs · approx. 4100 K"),
            _ => throw new ArgumentOutOfRangeException(nameof(illuminant)),
        };
        return $"{RawValue(illuminant)} — {(japanese ? ja : en)}";
    }
}
