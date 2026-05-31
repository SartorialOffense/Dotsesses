"""
Shared statistics helpers for the exploratory-stats plot modules
(significance_matrix.py, correlation_matrix.py).

ADR-0018 converges the Correlation and Significance Matrix tabs onto one
effect-size-led, raw-p inference frame. The significance stars are part of
that shared contract, so the single source of truth for the tier thresholds
lives here rather than inline in each module.
"""
import math
from typing import Optional


def significance_stars(p: Optional[float]) -> str:
    """
    Tiered significance stars for a RAW (uncorrected) p-value.

    Universal convention, identical on both tabs (ADR-0018 decision 4):
        p < .001  → '***'
        p < .01   → '**'
        p < .05   → '*'
        otherwise → ''   (not significant)

    Returns '' for an absent/undefined p (None or NaN) — the caller decides
    how an untestable cell is rendered (e.g. an em-dash), which is not a star
    concern. Stars are computed from raw p with no multiple-comparison
    correction (ADR-0018 decision 2).
    """
    if p is None or math.isnan(p):
        return ''
    if p < 0.001:
        return '***'
    if p < 0.01:
        return '**'
    if p < 0.05:
        return '*'
    return ''
