# Marten Skeleton Project

This is a lightweight skeleton project for testing Marten in an ASP.NET environment using event sourcing and different
projection types.

## 🎯 Goals

- Get hands-on experience with Marten's event sourcing features
- Try out different projection types (inline, async, live)
- Experiment with projection lifecycles (SingleStream, MultiStream, Daemon, etc.)
- Test event versioning and upcasting
- Automate registration of endpoints and projections using source generators

## 📦 Included Examples

- `PurchaseItems` use case (inventory, orders, confirmation emails)
- `InventoryAggregate` and `OrderAggregate` as event-sourced aggregates
- Inline projections for lookups (e.g. `ItemOrderLookupProjection`)
- Async projections for historical views
- Multi-stream projection example (e.g. Users assigned to Groups)
- Source generator for auto-mapping all `IEndpoint` implementations
- Source generator for enum of all `SingleStreamProjection<T>` types

## ✅ Todo List

- [ ] Add an aggregate that is only
- [ ] Set up basic `DocumentStore` configuration for Marten
- [ ] Implement `InventoryAggregate` and related events
- [ ] Implement `OrderAggregate` and related events
- [ ] Create the `PurchaseItems` use case
- [ ] Add inline projection for `ItemOrderLookupProjection`
- [ ] Add async projection for `OrderHistoryView`
- [ ] Add live projection for UI integration (e.g. SignalR + React Query)
- [ ] Create a `MultiStreamProjection` using `UserGroupId` as the key
- [ ] Create source generator for auto-mapping all `IEndpoint` implementations
- [ ] Create source generator for enum of all `SingleStreamProjection<T>` types
- [ ] Add an upcasting example with versioned events
- [ ] Write integration tests for use cases and projections
