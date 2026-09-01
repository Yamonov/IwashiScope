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
}
