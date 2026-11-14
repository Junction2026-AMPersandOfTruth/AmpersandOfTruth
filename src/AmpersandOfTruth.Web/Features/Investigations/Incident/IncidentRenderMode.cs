using Microsoft.AspNetCore.Components;

namespace AmpersandOfTruth.Web.Features.Investigations.Incident;

internal static class IncidentRenderMode
{
    public static bool IsInteractiveOrRendered(Func<RendererInfo> getRendererInfo, bool hasRendered)
    {
        if (hasRendered)
        {
            return true;
        }

        try
        {
            return getRendererInfo().IsInteractive;
        }
        catch
        {
            // bUnit leaves RendererInfo unset unless SetRendererInfo(...) is called explicitly.
            return false;
        }
    }
}
