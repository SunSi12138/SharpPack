# Raw Int32 JIT follow-up

The Reset helper candidate ff724136 passes full behavior gates but does not show
an acceptable overall performance result. Legacy raw Int32 slows in both
architectures. The unmanaged production path bypasses Reset, so this cannot
simply be attributed to executing the new helper. This JIT-only follow-up holds
A/B/C and the legacy harness fixed and inspects the raw callback and batch,
including possible effects of its unexecuted context branch. It reruns no
performance measurements and makes no production change. Separate eight-second
JIT processes do not establish the exact code used by every two-second timing
sample. Retain the complete original measurements and do not promote this
candidate on the basis of a proposed explanation.
