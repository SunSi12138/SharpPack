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

The remaining C# body templates still use `TypeMeta`. The next migration moves
fixed-size and exact-size eligibility into `SerializationPlan`, then moves wire
member operations, context dependencies, AOT roots, and the remaining wire
strategies. Only after all inputs used by a planned path are value snapshots
should an unchanged shape allow the generator to skip planning and emission.
