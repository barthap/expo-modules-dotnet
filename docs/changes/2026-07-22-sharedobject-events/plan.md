# Shared-object typed events plan

1. Generator support and diagnostics: add shared-object event modeling,
   generated property bodies, and generator tests. Commit the verified slice.
2. Runtime listener and dispatch support: reuse the event-emitter internals
   without changing module-event behavior, reacquire registry weak targets on
   the runtime thread, and add Hermes tests. Commit the verified slice.
3. Example and facade: emit `ExampleCounter` progress, consume it in the
   TypeScript facade, and add behavior tests for isolation, payloads, errors,
   zero listeners, release, and teardown. Commit verified logical slices.
4. Merge the accepted requirements into the living spec and authoring guide,
   archive this change directory under `docs/archive/changes/`, format, and
   commit the documentation slice.
