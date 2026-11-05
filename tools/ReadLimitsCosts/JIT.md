# Copy-path JIT probes

These separate probes use the exact Program.cs and project source from measured
harness commit `326bf672`, and unchanged A/B production revisions. They warm four
selected 64 KiB cases for eight seconds: empty-context allocation, ordinary ref
reuse, and matched cpblk allocation/reuse controls. Timings remain in the earlier
performance run; JIT probe processes do not contribute timing samples.

Both architectures use default tiered compilation/PGO. The script requires a
Tier1 RunBatch listing for each selected probe and prints structured method,
code-size, opcode and call summaries. Explicit name selection bounds output
independently of the runtime's ineffective assembly filter. Raw listings remain
temporary files. ARM64 indirect register targets may lack symbols; absence of
a named call is not proof that no such indirect call can occur.

Eight-second probe compilation does not prove the exact code version used in
the two-second benchmark processes. Matching summaries are not byte-identical
machine-code proof. Code growth or a different helper is a hypothesis to test,
not a causal performance result by itself. No production code is changed.
