# Original-HEAD compatibility corpus

`generate-original-head-corpus.sh` extracts the pinned original commit into an
isolated temporary directory, copies in the fixture, builds against that
unmodified source tree, and writes the golden payload corpus. It also serializes
the same system-type values with the current implementation and launches the
original executable in verification mode, so the old-reader/new-writer
direction is checked in a genuinely separate process.

Run from any directory:

```bash
tools/CompatibilityBaseline/generate-original-head-corpus.sh
```

The generator refuses to accept a corpus unless it came from commit
`85ab9ad76c380aca48c09ff3a0ad955ee5a2902b` and contains all 117 original
well-known formatter registrations, all 68 original generic shapes, and the
additional object, version-tolerant, circular-reference, static/external/dynamic
union, custom formatter, closed generic, custom collection, compression,
configuration, collection-value, and multidimensional-array cases.

The fixture intentionally uses the original non-generic API because it is
compiled only inside the extracted original source tree. The runtime project
does not restore that API.
