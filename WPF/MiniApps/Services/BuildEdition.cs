namespace MiniApps.Services;

public static class BuildEdition
{
#if MINIAPPS_DEVELOPER
    public const bool IsDeveloper = true;
#else
    public const bool IsDeveloper = false;
#endif
}
