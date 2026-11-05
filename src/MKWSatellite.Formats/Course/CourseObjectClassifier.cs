namespace MKWSatellite.Formats.Course;

/// <summary>
/// Maps actor type names to display categories. Name-based only: names are descriptive
/// (<c>CmnObj_DashBoardCinema000_A_00</c>) but gameplay behaviour still needs checking per type in code.
/// </summary>
public static class CourseObjectClassifier
{
    public static ObjectCategory Classify(string type)
    {
        bool Has(string s) => type.Contains(s, StringComparison.Ordinal);

        if (Has("ItemBox")) return ObjectCategory.ItemBox;
        if (Has("Coin") && !Has("CoinBattle")) return ObjectCategory.Coin;
        if (Has("DashBoard") || Has("DashRamp")) return ObjectCategory.DashPanel;
        if (Has("GlideBoard") || Has("GlideRamp")) return ObjectCategory.GlideRamp;
        if (Has("AccelRing") || Has("StarRing")) return ObjectCategory.BoostRing;
        if (Has("JumpRamp") || Has("JumpGap") || Has("JumpFloor") || Has("JumpLog") || Has("JumpSoil")) return ObjectCategory.JumpRamp;
        if (type == "StartPosition") return ObjectCategory.StartPosition;
        if (Has("LapGate")) return ObjectCategory.LapGate;
        if (Has("RescueArea")) return ObjectCategory.RespawnArea;
        if (Has("ValleyArea")) return ObjectCategory.ValleyArea;
        if (Has("AreaKiller") || Has("FallLavaArea") || Has("DeadArea") || Has("KillArea")) return ObjectCategory.KillArea;
        if (Has("HighSpeedArea")) return ObjectCategory.HighSpeedArea;
        if (type.StartsWith("Area_", StringComparison.Ordinal) || (type.StartsWith("SystemObj_", StringComparison.Ordinal) && Has("Area")))
            return ObjectCategory.OtherArea;
        if (Has("Fence") || Has("Barrier") || Has("Barricad") || Has("Pylon") || Has("Buoy") || Has("Bollard"))
            return ObjectCategory.Fence;
        if (Has("Audience") || Has("Cheering") || Has("CheerStick")) return ObjectCategory.Audience;
        if (Has("Mobcar") || Has("Mobship") || Has("Mobplane") || Has("Packun") || Has("Pukupuku") || Has("Dossun")
            || Has("Teresa") || Has("Thwomp") || Has("Cow000") || Has("Bull000") || Has("FireSnake") || Has("Flapper")
            || Has("Geyser") || Has("Poo000") || Has("Tokotoko") || Has("Patadon") || Has("Poihana") || Has("Penguin000")
            || Has("SnowBoarder") || Has("Forklift") || Has("TRex") || Has("Triceratops") || Has("Teren"))
            return ObjectCategory.Hazard;
        return ObjectCategory.Other;
    }
}
