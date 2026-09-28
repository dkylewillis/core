namespace Core.Import;

public static class Units
{
    // International foot and US survey foot relationship (exact).
    public const double UsSurveyFeetPerInternationalFoot = 1.000002000004;

    public static double ToProjectUnitsFactor(string linearUnits, string projectLinearUnits)
    {
        if (linearUnits == projectLinearUnits)
            return 1.0;
        // First build: project units are international feet; convert survey feet.
        if (projectLinearUnits == "feet" && linearUnits == "us-survey-feet")
            return UsSurveyFeetPerInternationalFoot;
        if (projectLinearUnits == "us-survey-feet" && linearUnits == "feet")
            return 1.0 / UsSurveyFeetPerInternationalFoot;
        return 1.0;
    }
}
