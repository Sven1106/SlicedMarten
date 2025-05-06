# Marten Skeleton Project

This is a lightweight skeleton project for testing Marten in an ASP.NET environment using event sourcing and different
projection types.

## 🎯 Goals

- Get hands-on experience with Marten's event sourcing features
- Try out different projection types (inline, async, live)
- Experiment with projection lifecycles (SingleStream, MultiStream, Daemon, etc.)

## ✅ Todo List

- [x] Append an event to a new stream through a command  
  → Using `StartStream` with a new `Guid` is clean and expressive. Perfect for initializing a new aggregate with its
  first event.

- [x] Get the current state from a single stream in a command.  
  → Reading current state inside commands via `FetchForWriting<T>` helps validate logic before appending new events.
  Keeps write flow intuitive.

- [x] Add a projection that applies events from a single stream.  
  → Single stream projections are ideal for rebuilding aggregate state. Using `SingleStreamProjection<T>` keeps
  projection logic isolated and easy to reason about.

- [x] Return a list of projections  
  → Use `query.Query<Projection>()` to return a list of projections.

- [ ] Return a list of projections with filtering

- [x] Return the latest projection for a stream  
  → Use `session.Events.FetchLatest<Projection>(streamId)` to return the most recent version of a projection for a
  specific stream.   
  → Supports **all projection lifecycles**:
    - **Inline**: uses the in-session state, no extra DB hit
    - **Live**: replays events using `AggregateStreamAsync`
    - **Async**: loads stored snapshot and applies remaining events

- [x] Add a projection that applies events from multiple streams.  
  → Multi stream projections make it possible to combine data from related streams (e.g. `User` and `UserGroup`, or
  `Order` and `Item`), but require good key coordination and handling of event slicing.

- [x] Append events to multiple streams in one command.  
  → Marten buffers all appends within a single `IDocumentSession` and persists them atomically on `SaveChangesAsync()`.

- [x] Use lookup table projections to support complex cross-stream projections  
  → Projections like `ItemToOrders` help correlate which streams are affected by external changes. Useful for reverse
  indexing relationships.

- [x] Find a way to notify UI, or third party services that a projection was updated.  
  → Marten subscriptions trigger on event append, not when projections are persisted — making it unreliable for
  notifying UI about the actual read model state.  
  → Chose PostgreSQL `LISTEN/NOTIFY`on projection tables to ensure signals are sent only after projections are fully
  updated, regardless of projection lifecycle.

- [x] Create changelog from events.  
  → All changes are logged as `FieldChange` objects directly from events. The current state is dynamically derived by traversing the changelog entries in reverse — no temporary or cached state is held inside the projection.
 
- [ ] DTO's vs Projection that has Ids that you need for a .

- [ ] Group by changable identifier in Multistream. E.g Name with counts on.

- [ ] Add a projection that gets data from other services.

- [ ] How should parallel created streams be constrained.

- [ ] Add upcasting for versioned events

- [ ] Handle projection deletions or cleanup when related aggregates are removed.

- [ ] Explore error handling and retries in projections.

- [ ] Try out inline vs async projection trade-offs in performance tests.



