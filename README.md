# Prop Firm Rules Playbook

A 12-page tactical playbook + printable cheat sheet covering every Apex Trader Funding rule that causes automatic failure.

**Live page:** https://reaver1000.github.io/prop-firm-playbook/

## What's Inside
- Trailing drawdown math with real examples
- Daily Loss Limit (DLL) circuit breaker tables
- 30% consistency rule breakdown
- Contract sizing by account tier ($25K–$150K)
- Overnight & weekend position rules
- Pre-trade checklist + emergency protocols
- BONUS: Prop-firm-safe NinjaTrader strategy (see `ninjatrader-strategies/`)

## Bonus: MNQ_ORB_PropFirm_Safe.cs
A NinjaTrader 8 strategy with built-in safeguards:
- Max contract limit clamp (respects Apex micro limits)
- Daily Loss Limit halt (auto-flattens when DLL hit)
- Trailing drawdown floor tracking
- Configurable per account tier

## Disclaimer
Not financial advice. Trading involves substantial risk of loss. Always verify current rules directly with your prop firm.
