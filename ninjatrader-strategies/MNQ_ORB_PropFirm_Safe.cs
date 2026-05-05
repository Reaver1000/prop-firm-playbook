using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Prop-Firm-Safe MNQ ORB Strategy.
    /// Built-in safeguards: max contract limit, daily loss limit halt, trailing drawdown floor.
    /// This is a NEW file in the income-automation product suite.
    /// </summary>
    public class MNQ_ORB_PropFirm_Safe : Strategy
    {
        private double rangeHigh = 0;
        private double rangeLow = double.MaxValue;
        private bool longTaken = false;
        private bool shortTaken = false;
        private bool rangeLocked = false;

        // --- Risk Parameters ---
        [NinjaScriptProperty]
        [Display(Name = "Dollar Risk Per Trade", GroupName = "Risk", Order = 0)]
        public double MyRiskAmount { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit:Risk Ratio", GroupName = "Risk", Order = 1)]
        public double ProfitRiskRatio { get; set; }

        // --- Prop Firm Limits ---
        [NinjaScriptProperty]
        [Display(Name = "Max Contracts", GroupName = "Prop Firm Limits", Order = 0)]
        public int MaxContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Daily Loss Limit ($)", GroupName = "Prop Firm Limits", Order = 1)]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trailing Drawdown ($)", GroupName = "Prop Firm Limits", Order = 2)]
        public double TrailingDrawdown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Account Start Balance ($)", GroupName = "Prop Firm Limits", Order = 3)]
        public double AccountStartBalance { get; set; }

        // --- Runtime Safety State ---
        private double dailyRealizedPnL = 0;
        private double peakEquity = 0;
        private double drawdownFloor = 0;
        private bool halted = false;
        private DateTime lastTradeDate = DateTime.MinValue;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MNQ_ORB_PropFirm_Safe";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 2;

                // Defaults for Apex $50K
                MyRiskAmount = 250;
                ProfitRiskRatio = 1.5;
                MaxContracts = 40;
                DailyLossLimit = 1250;
                TrailingDrawdown = 2500;
                AccountStartBalance = 50000;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.Historical || State == State.Realtime)
            {
                dailyRealizedPnL = 0;
                peakEquity = AccountStartBalance;
                drawdownFloor = AccountStartBalance - TrailingDrawdown;
                halted = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 5 || CurrentBars[1] < 5) return;

            // --- 1-MIN CHART: Build Opening Range ---
            if (BarsInProgress == 1)
            {
                int t = ToTime(Times[1][0]);
                if (t == 91600)
                {
                    rangeHigh = Highs[1][0];
                    rangeLow = Lows[1][0];
                    rangeLocked = false;
                }
                else if (t > 91600 && t <= 93000)
                {
                    rangeHigh = Math.Max(rangeHigh, Highs[1][0]);
                    rangeLow = Math.Min(rangeLow, Lows[1][0]);
                }
                if (t == 93000 && !rangeLocked) rangeLocked = true;
                return;
            }

            // --- PRIMARY CHART ---
            if (BarsInProgress != 0) return;
            int currentTime = ToTime(Time[0]);

            // Session reset
            if (Bars.IsFirstBarOfSession && IsFirstTickOfBar)
            {
                rangeHigh = 0;
                rangeLow = double.MaxValue;
                longTaken = false;
                shortTaken = false;
                rangeLocked = false;
                dailyRealizedPnL = 0;
                peakEquity = AccountStartBalance;
                drawdownFloor = AccountStartBalance - TrailingDrawdown;
                halted = false;
            }

            // Track realized PnL from closed trades via Strategy method
            double realizedToday = StrategiesPerformance[0].RealtimeTrades.TradesPerformance.NetProfit;
            if (lastTradeDate != Time[0].Date)
            {
                dailyRealizedPnL = 0;
                lastTradeDate = Time[0].Date;
            }
            dailyRealizedPnL = realizedToday;

            double unrealized = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
            double equity = AccountStartBalance + dailyRealizedPnL + unrealized;

            // Update peak equity and trailing floor
            if (equity > peakEquity) peakEquity = equity;
            drawdownFloor = peakEquity - TrailingDrawdown;

            // --- SAFETY HALTS ---
            if (dailyRealizedPnL <= -DailyLossLimit) halted = true;
            if (equity <= drawdownFloor) halted = true;

            // Flatten if halted
            if (halted && Position.MarketPosition != MarketPosition.Flat)
            {
                ExitLong("SafetyExit", "DLL/Drawdown Halt");
                ExitShort("SafetyExit", "DLL/Drawdown Halt");
                Draw.Text(this, "Halt" + CurrentBar, false, "HALTED", 0, High[0], 20, Brushes.Red,
                          new SimpleFont("Arial", 14), Brushes.Transparent, Brushes.Transparent, 0);
            }

            // Draw range
            if (rangeLocked && currentTime == 93000)
            {
                string tag = Time[0].ToString("yyyyMMdd");
                Draw.Line(this, "H" + tag, false, 0, rangeHigh, -50, rangeHigh, Brushes.Goldenrod, DashStyleHelper.Solid, 2);
                Draw.Line(this, "L" + tag, false, 0, rangeLow, -50, rangeLow, Brushes.Goldenrod, DashStyleHelper.Solid, 2);
            }

            // Time exit
            if (currentTime >= 120000 && Position.MarketPosition != MarketPosition.Flat)
            {
                ExitLong("TimeExit", "");
                ExitShort("TimeExit", "");
            }

            // --- ENTRY LOGIC (with prop firm safety) ---
            if (!rangeLocked || currentTime <= 93000 || currentTime >= 120000) return;
            if (Position.MarketPosition != MarketPosition.Flat) return;
            if (halted) return;

            string tradeTag = CurrentBar.ToString();

            if (Close[0] > rangeHigh && !longTaken)
            {
                double sl = Low[0];
                double risk = Close[0] - sl;
                if (risk <= 0) return;

                int qty = (int)Math.Floor(MyRiskAmount / (risk * 2));
                qty = Math.Min(qty, MaxContracts);
                if (qty <= 0) return;

                SetStopLoss("L", CalculationMode.Price, sl, false);
                SetProfitTarget("L", CalculationMode.Price, Close[0] + risk * ProfitRiskRatio);
                Draw.Rectangle(this, "LSL" + tradeTag, false, 0, Close[0], -10, sl, Brushes.Transparent, Brushes.DarkRed, 20);
                Draw.Rectangle(this, "LTP" + tradeTag, false, 0, Close[0], -10, Close[0] + risk * ProfitRiskRatio, Brushes.Transparent, Brushes.DarkGreen, 20);
                EnterLong(qty, "L");
                longTaken = true;
            }
            else if (Close[0] < rangeLow && !shortTaken)
            {
                double sl = High[0];
                double risk = sl - Close[0];
                if (risk <= 0) return;

                int qty = (int)Math.Floor(MyRiskAmount / (risk * 2));
                qty = Math.Min(qty, MaxContracts);
                if (qty <= 0) return;

                SetStopLoss("S", CalculationMode.Price, sl, false);
                SetProfitTarget("S", CalculationMode.Price, Close[0] - risk * ProfitRiskRatio);
                Draw.Rectangle(this, "SSL" + tradeTag, false, 0, Close[0], -10, sl, Brushes.Transparent, Brushes.DarkRed, 20);
                Draw.Rectangle(this, "STP" + tradeTag, false, 0, Close[0], -10, Close[0] - risk * ProfitRiskRatio, Brushes.Transparent, Brushes.DarkGreen, 20);
                EnterShort(qty, "S");
                shortTaken = true;
            }
        }
    }
}
