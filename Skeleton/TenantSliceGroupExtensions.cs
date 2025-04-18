using Marten.Events;
using Marten.Events.Aggregation;

namespace Skeleton;

public static class TenantSliceGroupExtensions
{
    public static bool TryAddEvent<TDoc, TId>(this TenantSliceGroup<TDoc, TId> sliceGroup, TId id, IEvent @event)
    {
        if (sliceGroup.Slices.TryFind(id, out var slice) && slice.Events().Any(e => e.Id == @event.Id)) return false;

        sliceGroup.AddEvent(id, @event);
        return true;
    }
}