# From declarations to generated code

`AnalyzeModel` resolves the attributed declaration and returns diagnostics
separately from the `TypeShape` of a valid model. The shape records the
declaration, serialization target, ordered members, constructor parameters,
callbacks, union cases, and typed custom formatter arguments. It also carries
the declaration facts needed to render nested types, generic constraints, and
collection formatters. It retains no symbols, syntax, or compilation.

`SerializationPlanner` chooses one root path: fixed-size, exact-size or general.
It records the effective wire slots, including holes for versioned models,
and maps constructor arguments to those slots in parameter order. Analysis
rejects orders that do not fit the versioned object header. Raw unmanaged
values have no slots.

The plan also groups consecutive unmanaged slots within Core's overload limit
and chooses inline or buffered length encoding for versioned writes. It
exposes whether the model is versioned and whether it tracks references.
Context-aware writers still use the existing buffered path because an override
can change the encoded length.

The context dependency set records the ordered factory lookups for the
effective wire slots. If the set is empty, no override formatter is emitted.
Analysis follows arrays and known generic formatter types and does not
traverse a member with an explicit custom formatter.

The AOT root set separates typed factories from closed formatter instances.
Analysis records which model factories can provide context; the planner sorts
and deduplicates the dependencies. The emitter preserves typed factories for
trimming and keeps closed formatter instances only without dynamic code.

Analysis also captures the names already in use and whether a static initializer
is required. Planning chooses available helper names and the self-factory root
form. The model templates render those names without searching for collisions.

`CSharpEmitter` renders from the plan and the namespace syntax option, without
access to Roslyn symbols or the compilation. External union target factories
also use an immutable plan: closed matches are ordered before the optional
open fallback. The representative generated-source and runtime-dependency
snapshots must stay unchanged when a decision moves between stages.

## What reruns after an edit

Semantic extraction reads the compilation so related changes, such as a new
type for an inherited member, are visible. Equal model shapes keep planning
and rendering cached. Diagnostics are reported separately so their source
locations stay current. The optional serialization-information file has a
separate output path; changing its directory does not render C# again.

`IncrementalGeneratorTest` tracks the real provider steps across an unrelated
file edit and a dependency edit. It also checks that external factory rendering
stays cached when its plan does not change.
