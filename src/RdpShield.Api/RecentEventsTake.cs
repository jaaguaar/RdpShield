namespace RdpShield.Api;

public static class RecentEventsTake
{
    public const int Default = 50;
    public const int Min = 1;
    public const int Max = 500;

    public static int Normalize(int requested)
    {
        if (requested <= 0)
            return Default;

        if (requested > Max)
            return Max;

        return requested;
    }
}
