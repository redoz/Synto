# Quality Standards

Shared scoring model used by all review and evaluation skills. Defines how
findings are classified, how dimensions are scored, and how verdicts are
determined.

## Severity Definitions

| Severity     | Meaning |
|--------------|---------|
| **critical** | Broken or will break — wrong generated code, generator crash, broken incremental caching, corrupted state |
| **high**     | Significant problem — wrong behavior under realistic conditions, major design flaw |
| **medium**   | Real but contained — awkward code, missing edge case, suboptimal pattern |
| **low**      | Nit — style, naming, minor inconsistency, opportunity for improvement |

## Quality Dimensions

Four dimensions, each evaluating a distinct aspect of code quality:

| Dimension         | Question it answers |
|-------------------|---------------------|
| Maintainability   | Can we understand, modify, and extend this code? |
| Correctness       | Does the code do what it's supposed to do? |
| Performance       | Does the code use resources appropriately? |
| Testability       | Is the code structured for effective testing? |

## Domain Experts

Domain experts are cross-cutting reviewers who bring deep domain knowledge
rather than evaluating a single quality dimension. Their findings are attributed
to whichever dimension they most affect and count toward that dimension's score.

Domain experts do not have their own column in the scorecard. Instead, they have
a dedicated section in the report that collects all their findings with
dimension tags, providing a coherent domain-lens view.

| Domain Expert        | Domain | Rules file |
|----------------------|--------|------------|
| principal-engineer   | Architectural consequences | `.claude/rules/consequences.md` |

## Dimension Scores (for full evaluations)

| Score              | Criteria |
|--------------------|----------|
| **Excellent**      | No high or critical issues. Few or no medium. |
| **Good enough**    | No critical issues. Few high at most. |
| **Needs improvement** | Has high issues, or many medium issues. |
| **Broken**         | Has critical issues. |

## Quality Gate Tiers (for full evaluations)

Dimensions are not weighted equally:

1. **Hard gates** — a single critical issue fails the evaluation:
   - Correctness
2. **Must be Good enough or better:**
   - Maintainability
   - Performance
3. **Must be Needs improvement or better** (not Broken):
   - Testability

The evaluation passes if all three tiers are met.

## Review Verdicts (for change reviews)

| Verdict       | Meaning |
|---------------|---------|
| **RETHINK**   | Wrong approach or broken architecture. Either the direction is fundamentally wrong, or there are boundary violations, abstraction leaks, or wrong layering. Do not proceed — fix the approach before reviewing details. |
| **NEEDS WORK**| Approach is sound but has high findings in other dimensions. Address before merging. |
| **LGTM**   | No critical or high findings. Safe to merge. |

RETHINK is determined before detail review runs. NEEDS WORK and LGTM are
determined after all dimensions are reviewed.
