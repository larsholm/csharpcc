# Phase 7 validation

Status: all six remote CI jobs passed with 261 tests per job in
[run 35533151294](https://github.com/larsholm/csharpcc/actions/runs/35533151294).
Final verification is rerunning after adding generic parser compatibility tests.

## Local verification

- .NET SDK 10.0.111 on Linux: 261 solution tests passed in Debug and Release;
  the final 267-test suite includes generic parser compatibility coverage.
- Release tests run from a clean source copy at a path containing spaces, without
  bin/obj output from the working checkout.
- The newly built clean generator regenerates all seven bootstrap files identically.
- All 14 isolated Roslyn boundary checks pass.
- The 864-entry public/protected API snapshot and historical token-constants
  snapshot pass. Modern and legacy consumer API/regeneration checks pass.
- Integration consumers compile without CSharpCC or Roslyn references. Modern
  consumers enforce nullable and selected generated-warning categories as errors.
- The 11-scenario diagnostic/output comparison retains exit codes and constants;
  intentional source/layout/NFA changes are detailed in the phase 6 report.

The GitHub Actions matrix now checks Debug and Release on Linux, Windows, and
macOS. Each job uses a checkout path containing spaces, verifies bootstrap output,
regenerates it, rebuilds/tests, and verifies second-generation output again.
The final run will be recorded below after it completes. Windows consumers set
UTF-8 console output explicitly so the test transport preserves Unicode values
independently of the machine's console code page.

## Generated-consumer performance

The reproducible [benchmark tool](../tools/GenerationBenchmark/README.md) builds
standalone .NET 10 Release consumers, checks return values, and verifies that their
output contains no CSharpCC/Roslyn assemblies. Each case uses 500 warmups and seven
batches of 3,000 parses, with tiered compilation disabled. Timings include ReInit
and a new StringReader; managed allocation totals include all managed threads.

The baseline is the preserved .NET 10 CLI from before phase 5. Two legacy runs were
made in opposite generator order; the table shows their median ranges. Modern
mode has one measured run. These short, synthetic workloads cannot establish
performance for every grammar, and the small timing variation is not a speedup claim.

| Consumer | 20 words per parse | 200 words per parse | Bytes/parse, 20 words | Bytes/parse, 200 words |
| --- | ---: | ---: | ---: | ---: |
| Baseline legacy | 4.37–4.39 µs | 11.54–11.66 µs | 2,840 | 20,120 |
| Current legacy | 4.33–4.60 µs | 11.47–12.24 µs | 2,840 | 20,120 |
| Current C# 14 | 4.51 µs | 13.12 µs | 2,840 | 20,120 |

Legacy timing ranges overlap the baseline. Allocation is unchanged in both output
modes for these fixtures. The modern 200-word case is slower in this run; modern
output introduces explicit nullable-state guards and is opt-in. No consumer
performance improvement is claimed. Generator time and allocations, including
the additional Roslyn cost and the measured boundary optimization, are recorded
in [phase 6](MODERNIZATION_PHASE_6.md).

## Remaining evidence

Actual GitHub CI completion on all supported platforms is required before marking
the modernization plan complete. Existing warnings from legacy output and the
bootstrap remain visible; handwritten nullable warnings and selected modern
consumer warning categories are enforced rather than broadly suppressed.
