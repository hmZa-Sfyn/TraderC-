using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace TraderDev
{
    public partial class Form1 : Form
    {
        private Timer priceUpdateTimer;
        private Random random = new Random();
        private double initialPrice = 100.0;
        private string priceSavePath = "./historic_prices.json";
        private string walletSavePath = "./wallet.json";

        private List<PricePoint> priceHistoryAll = new List<PricePoint>();
        private int currentViewLength = 10;

        // Price simulation vars
        private double volatility = 0.2; // Annual volatility (20%)
        private double drift = 0.05; // Annual drift (5% expected return)
        private const double timeStep = 1.0 / 252; // One trading day (252 trading days per year)
        private const double wickFactor = 0.02; // Controls size of high/low wicks (2% of price)

        // Session tracking
        private double sessionStartPrice;

        // Trading wallet
        private Wallet wallet;

        public double currentprice = 0;

        public Form1()
        {
            InitializeComponent();
            InitializeChart();
            LoadPriceHistory();
            LoadWallet();

            sessionStartPrice = priceHistoryAll.Last().Close;

            InitializeTimer();
            UpdateTradingLabels();
            UpdateWalletAnalyticsLabel();
        }

        private void InitializeChart()
        {
            chart1.Series.Clear();

            Series series = new Series("PriceSeries")
            {
                ChartType = SeriesChartType.Candlestick,
                YValuesPerPoint = 4 // High, Low, Open, Close
            };

            // Customize candlestick appearance
            series["PriceUpColor"] = "Green";
            series["PriceDownColor"] = "Red";
            series.BorderColor = Color.Black;
            series.BorderWidth = 1;
            series["PointWidth"] = "0.9"; // Close candles (90% of space)
            series["PixelPointWidth"] = "8"; // Narrow candles for aesthetics

            chart1.Series.Add(series);
            chart1.ChartAreas[0].AxisX.Title = "Time";
            chart1.ChartAreas[0].AxisY.Title = "Price";

            // Rotate X-axis labels by 45 degrees
            chart1.ChartAreas[0].AxisX.LabelStyle.Angle = 45;
            // Enable grid lines
            chart1.ChartAreas[0].AxisX.MajorGrid.LineColor = Color.LightGray;
            chart1.ChartAreas[0].AxisY.MajorGrid.LineColor = Color.LightGray;
        }

        private void InitializeTimer()
        {
            priceUpdateTimer = new Timer();
            priceUpdateTimer.Interval = 1200;
            priceUpdateTimer.Tick += PriceUpdateTimer_Tick;
            priceUpdateTimer.Start();
        }

        private void PriceUpdateTimer_Tick(object sender, EventArgs e)
        {
            double lastClose = priceHistoryAll.Count > 0 ? priceHistoryAll[priceHistoryAll.Count - 1].Close : initialPrice;
            var newPricePoint = GenerateRealisticPrice(lastClose);

            priceHistoryAll.Add(newPricePoint);

            SavePriceHistory();
            UpdateChart();
            UpdateLabels();
            UpdateWalletAnalyticsLabel();
        }

        private PricePoint GenerateRealisticPrice(double lastClose)
        {
            // Price simulation using Geometric Brownian Motion (GBM)
            // Formula: S_t = S_(t-1) * exp((drift - 0.5 * volatility^2) * dt + volatility * sqrt(dt) * Z)
            // Where:
            // - S_t: New price (Close)
            // - S_(t-1): Previous price (lastClose)
            // - drift: Expected return (annualized)
            // - volatility: Price volatility (annualized)
            // - dt: Time step (fraction of a year, e.g., 1/252 for one trading day)
            // - Z: Standard normal random variable (random.NextGaussian)
            // This models log-normal price movements, common in financial markets.
            // High and Low are generated as small deviations from Open and Close to create realistic candlesticks.

            // Generate random normal variable (Box-Muller transform for Gaussian)
            double u1 = random.NextDouble();
            double u2 = random.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            // Calculate Close price using GBM
            double driftTerm = (drift - 0.5 * volatility * volatility) * timeStep;
            double volatilityTerm = volatility * Math.Sqrt(timeStep) * z;
            double closePrice = lastClose * Math.Exp(driftTerm + volatilityTerm);
            if (closePrice < 0.1) closePrice = 0.1; // Prevent negative or zero prices

            // Open price matches previous Close for continuity
            double openPrice = lastClose;

            // Generate High and Low with small, realistic variations
            double maxBody = Math.Abs(closePrice - openPrice);
            double wickSize = closePrice * wickFactor; // Wicks are proportional to price
            double highPrice = Math.Max(openPrice, closePrice) + (random.NextDouble() * wickSize);
            double lowPrice = Math.Min(openPrice, closePrice) - (random.NextDouble() * wickSize);
            if (lowPrice < 0.1) lowPrice = 0.1; // Ensure non-negative prices

            bool isUp = closePrice >= openPrice;

            // Update UI labels
            double changeInPrice = Math.Abs(closePrice - lastClose);
            lbl_newprice_volume.Text = changeInPrice.ToString("F2");
            lbl_newprice_volume.ForeColor = closePrice >= lastClose ? Color.Green : Color.Red;

            double percentChange = ((closePrice / lastClose) * 100) - 100;
            lbl_is_this_price_profit_from_last_one.Text = $"{percentChange:F2}%";
            lbl_is_this_price_profit_from_last_one.ForeColor = closePrice >= lastClose ? Color.Green : Color.Red;

            currentprice = closePrice;

            double userCanBuy = wallet.CashBalance / closePrice;
            int usrCanBuy = (int)userCanBuy;
            btn_but_100000.Text = usrCanBuy.ToString();

            return new PricePoint
            {
                High = highPrice,
                Low = lowPrice,
                Open = openPrice,
                Close = closePrice,
                IsUp = isUp
            };
        }

        private void UpdateChart()
        {
            List<PricePoint> viewData = priceHistoryAll.Skip(Math.Max(0, priceHistoryAll.Count - currentViewLength)).ToList();

            var series = chart1.Series["PriceSeries"];
            series.Points.Clear();

            for (int i = 0; i < viewData.Count; i++)
            {
                var point = viewData[i];
                // Use zero-based index for continuous candles
                series.Points.AddXY(i, point.High, point.Low, point.Open, point.Close);
            }

            // Dynamic Y-axis scaling
            if (viewData.Count > 0)
            {
                double currentPrice = viewData.Last().Close;
                double minPrice = viewData.Min(p => p.Low);
                double maxPrice = viewData.Max(p => p.High);
                double priceRange = maxPrice - minPrice;

                double padding = priceRange * 0.05; // Tight padding
                if (padding == 0) padding = currentPrice * 0.05;

                double yMin, yMax, interval;

                if (currentViewLength <= 30)
                {
                    double range = currentViewLength == 10 ? 0.5 : 2.0; // Tighter for short views
                    yMin = Math.Floor(currentPrice - range);
                    yMax = Math.Ceiling(currentPrice + range);
                    interval = currentViewLength == 10 ? 0.05 : 0.2;
                }
                else
                {
                    yMin = Math.Floor(minPrice - padding);
                    yMax = Math.Ceiling(maxPrice + padding);
                    interval = Math.Ceiling((yMax - yMin) / 10);
                    if (currentViewLength >= 1000)
                    {
                        interval = 5;
                        yMin = Math.Floor(yMin / 5) * 5;
                        yMax = Math.Ceiling(yMax / 5) * 5;
                    }
                }

                if (yMin < 0) yMin = 0;

                chart1.ChartAreas[0].AxisY.Minimum = yMin;
                chart1.ChartAreas[0].AxisY.Maximum = yMax;
                chart1.ChartAreas[0].AxisY.Interval = interval;

                // Ensure continuous X-axis
                chart1.ChartAreas[0].AxisX.Interval = 1;
                chart1.ChartAreas[0].AxisX.Minimum = 0;
                chart1.ChartAreas[0].AxisX.Maximum = viewData.Count;
            }
        }

        private void SavePriceHistory()
        {
            try
            {
                File.WriteAllText(priceSavePath, JsonConvert.SerializeObject(priceHistoryAll, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save price data: " + ex.Message);
            }
        }

        private void LoadPriceHistory()
        {
            try
            {
                if (File.Exists(priceSavePath))
                {
                    string json = File.ReadAllText(priceSavePath);
                    priceHistoryAll = JsonConvert.DeserializeObject<List<PricePoint>>(json);
                }

                if (priceHistoryAll == null || priceHistoryAll.Count == 0)
                {
                    priceHistoryAll = new List<PricePoint>
                    {
                        new PricePoint
                        {
                            High = initialPrice,
                            Low = initialPrice,
                            Open = initialPrice,
                            Close = initialPrice,
                            IsUp = true
                        }
                    };
                }
            }
            catch
            {
                priceHistoryAll = new List<PricePoint>
                {
                    new PricePoint
                    {
                        High = initialPrice,
                        Low = initialPrice,
                        Open = initialPrice,
                        Close = initialPrice,
                        IsUp = true
                    }
                };
            }
        }

        private void SaveWallet()
        {
            try
            {
                File.WriteAllText(walletSavePath, JsonConvert.SerializeObject(wallet, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save wallet data: " + ex.Message);
            }
        }

        private void LoadWallet()
        {
            try
            {
                if (File.Exists(walletSavePath))
                {
                    string json = File.ReadAllText(walletSavePath);
                    wallet = JsonConvert.DeserializeObject<Wallet>(json);
                }

                if (wallet == null)
                {
                    wallet = new Wallet()
                    {
                        CashBalance = 100000.0,
                        StockHoldings = 0,
                        InitialCash = 100000.0
                    };
                }
            }
            catch
            {
                wallet = new Wallet()
                {
                    CashBalance = 100000.0,
                    StockHoldings = 0,
                    InitialCash = 100000.0
                };
            }
        }

        private void UpdateLabels()
        {
            if (priceHistoryAll.Count == 0) return;

            var currentPrice = priceHistoryAll.Last().Close;
            var firstPrice = priceHistoryAll[0].Close;

            lbl_current_price.Text = $"{currentPrice:F2}";

            double percentChange = ((currentPrice - firstPrice) / firstPrice) * 100.0;
            lbl_analytics_from_last_10th_price.Text = $"{percentChange:+0.00;-0.00;0.00}%";

            if (currentPrice > firstPrice)
            {
                lbl_current_price.ForeColor = Color.Green;
                lbl_analytics_from_last_10th_price.ForeColor = Color.Green;
            }
            else if (currentPrice < firstPrice)
            {
                lbl_current_price.ForeColor = Color.Red;
                lbl_analytics_from_last_10th_price.ForeColor = Color.Red;
            }
            else
            {
                lbl_current_price.ForeColor = Color.Black;
                lbl_analytics_from_last_10th_price.ForeColor = Color.Black;
            }

            double priceDiffFromSessionStart = currentPrice - sessionStartPrice;
            string sign = priceDiffFromSessionStart >= 0 ? "+" : "-";
            lbl_price_change_from_the_start_of_session.Text = $"{sign}{Math.Abs(priceDiffFromSessionStart):F2}";

            if (priceDiffFromSessionStart > 0)
            {
                lbl_price_change_from_the_start_of_session.ForeColor = Color.Green;
            }
            else if (priceDiffFromSessionStart < 0)
                lbl_price_change_from_the_start_of_session.ForeColor = Color.Red;
            else
                lbl_price_change_from_the_start_of_session.ForeColor = Color.Black;
        }

        private double previousStockValue = 0;

        private void UpdateTradingLabels()
        {
            double currentPrice = GetCurrentPrice();
            int stocks = wallet.StockHoldings;
            double stockValue = stocks * currentPrice;

            lbl_cash_balance.Text = $"Cash: ${wallet.CashBalance:F2}";
            lbl_stock_holdings.Text = $"Stocks: {stocks} (${stockValue:F2})";

            if (stockValue > previousStockValue)
                lbl_stock_holdings.ForeColor = Color.Green;
            else if (stockValue < previousStockValue)
                lbl_stock_holdings.ForeColor = Color.Red;
            else
                lbl_stock_holdings.ForeColor = Color.Black;

            previousStockValue = stockValue;
        }

        private void UpdateWalletAnalyticsLabel()
        {
            double currentPrice = GetCurrentPrice();
            double currentPortfolioValue = wallet.CashBalance + wallet.StockHoldings * currentPrice;
            double initialPortfolioValue = wallet.InitialCash;

            double profitLoss = currentPortfolioValue - initialPortfolioValue;
            double profitLossPercent = (profitLoss / initialPortfolioValue) * 100.0;

            lbl_wallet_analytics.Text = $"P/L: {profitLoss:+0.00;-0.00;0.00} (${profitLossPercent:+0.00;-0.00;0.00}%)";

            if (profitLoss > 0)
                lbl_wallet_analytics.ForeColor = Color.Green;
            else if (profitLoss < 0)
                lbl_wallet_analytics.ForeColor = Color.Red;
            else
                lbl_wallet_analytics.ForeColor = Color.Black;
        }

        private double GetCurrentPrice()
        {
            return priceHistoryAll.Count > 0 ? priceHistoryAll.Last().Close : initialPrice;
        }

        private void BuyStock(int quantity)
        {
            double price = GetCurrentPrice();
            double cost = price * quantity;

            if (cost > wallet.CashBalance)
            {
                MessageBox.Show($"Not enough cash to buy {quantity} stocks.");
                return;
            }

            wallet.CashBalance -= cost;
            wallet.StockHoldings += quantity;

            SaveWallet();
            UpdateTradingLabels();
            UpdateWalletAnalyticsLabel();
        }

        private void SellStock(int quantity)
        {
            if (quantity > wallet.StockHoldings)
            {
                MessageBox.Show($"Not enough stocks to sell {quantity}.");
                return;
            }

            double price = GetCurrentPrice();
            double revenue = price * quantity;

            wallet.CashBalance += revenue;
            wallet.StockHoldings -= quantity;

            SaveWallet();
            UpdateTradingLabels();
            UpdateWalletAnalyticsLabel();
        }

        private void btn_but_1_Click(object sender, EventArgs e) => BuyStock(1);
        private void btn_sell_1_Click(object sender, EventArgs e) => SellStock(1);
        private void btn_but_10_Click(object sender, EventArgs e) => BuyStock(10);
        private void btn_sell_10_Click(object sender, EventArgs e) => SellStock(10);
        private void btn_but_100_Click(object sender, EventArgs e) => BuyStock(100);
        private void btn_sell_100_Click(object sender, EventArgs e) => SellStock(100);
        private void btn_but_1000_Click(object sender, EventArgs e) => BuyStock(1000);
        private void btn_sell_1000_Click(object sender, EventArgs e) => SellStock(1000);
        private void btn_but_10000_Click(object sender, EventArgs e) => BuyStock(10000);
        private void btn_sell_10000_Click(object sender, EventArgs e) => SellStock(10000);

        private void btn_show_last_10_prices_Click(object sender, EventArgs e) => SetViewLength(10);
        private void btn_show_last_30_prices_Click(object sender, EventArgs e) => SetViewLength(30);
        private void btn_show_last_100_prices_Click(object sender, EventArgs e) => SetViewLength(100);
        private void btn_show_last_500_prices_Click(object sender, EventArgs e) => SetViewLength(500);
        private void btn_show_last_1000_prices_Click(object sender, EventArgs e) => SetViewLength(1000);
        private void btn_show_last_2000_prices_Click(object sender, EventArgs e) => SetViewLength(2000);
        private void btn_show_last_10000_prices_Click(object sender, EventArgs e) => SetViewLength(10000);
        private void btn_show_last_100000_prices_Click(object sender, EventArgs e) => SetViewLength(100000);
        private void btn_show_last_1000000_prices_Click(object sender, EventArgs e) => SetViewLength(1000000);
        private void btn_show_last_10000000_prices_Click(object sender, EventArgs e) => SetViewLength(10000000);

        private void SetViewLength(int length)
        {
            currentViewLength = length;
            UpdateChart();
        }

        private void btn_but_100000_Click(object sender, EventArgs e)
        {
            double userCanBuy = wallet.CashBalance / currentprice;
            int usrCanBuy = (int)userCanBuy;
            btn_but_100000.Text = usrCanBuy.ToString();
            BuyStock(usrCanBuy);
        }

        private void btn_sell_100000_Click(object sender, EventArgs e) => SellStock(100000);

        private void chart1_Click(object sender, EventArgs e)
        {
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }
    }

    public class PricePoint
    {
        public double High { get; set; }
        public double Low { get; set; }
        public double Open { get; set; }
        public double Close { get; set; }
        public bool IsUp { get; set; }
    }

    public class Wallet
    {
        public double CashBalance { get; set; }
        public int StockHoldings { get; set; }
        public double InitialCash { get; set; }
    }
}