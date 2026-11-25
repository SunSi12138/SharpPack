# From declarations to generated code

`AnalyzeModel` resolves the attributed declaration, reports the existing
diagnostics, and extracts a `TypeShape` for a valid model. The shape is an
immutable value snapshot: it records the declaration and serialization
target, ordered members, constructor parameters, callbacks, union cases, and
typed custom formatter arguments. It does not retain symbols, syntax or
compilations. Two compilations with the same recorded facts produce equal
shapes.

The C# generator uses the shape for its file identity, namespace, serialization
remarks, callback calls, and custom formatter declarations. In an external
union the declaring formatter and the serialization target remain distinct.
The representative source and generated runtime dependency snapshots must not
change when moving a decision between phases.

`SerializationPlanner` chooses one root path: fixed-size, exact-size or general.
The C# emitter renders the chosen interfaces and methods instead of repeating
eligibility checks. The remaining C# body templates still use `TypeMeta`; wire
slots are now explicit in the plan, including unoccupied slots for versioned
models. When a model uses a versioned object header, analysis rejects orders
that cannot fit before the planner creates those slots. Raw unmanaged values
have no slots. The emitter uses the slot sequence without changing the declared
members.

The plan also groups consecutive unmanaged slots within Core's overload limit
and chooses inline or buffered length encoding for versioned writes.
Context-aware writers still use the existing buffered path because an override
can change the encoded length.

The context dependency set records the ordered factory lookups and whether
the effective slots need an override formatter. Analysis follows arrays and
known generic formatter types once and does not traverse a member with an
explicit custom formatter.

The AOT root set separates typed factories from closed formatter instances.
Analysis records which model factories can provide context; the planner sorts
and deduplicates the dependencies. The emitter preserves typed factories for
trimming and keeps closed formatter instances only without dynamic code.

Remaining per-member operations, generated root names and other wire strategies
move to the plan next. An unchanged shape can skip planning and emission only
after it captures every input those stages actually use.
