using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.ObjectModel;

namespace KuyrukSimulasyonu
{
    public partial class MainWindow : Window
    {
        // === Model ===
        public class QueueRecord
        {
            public string PointId { get; set; } = "BN01";   // Bekleme Noktası
            public DateTime Timestamp { get; set; }         // Fotoğraf zamanı
            public int DurationMin { get; set; }            // Kuyruk süresi (dk)
        }

        // === Durum ===
        private readonly ObservableCollection<QueueRecord> _records = new();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly List<DateTime> _frames = new();     // benzersiz zaman kareleri (5 dk adımlar)
        private int _frameIndex = 0;

        // Renk paleti: her BN için sabit renk
        private readonly Dictionary<string, Brush> _palette = new();
        private readonly Brush[] _colors = new Brush[]
        {
            Brushes.LightBlue, Brushes.LightGreen, Brushes.LightCoral, Brushes.Khaki,
            Brushes.Plum, Brushes.Orange, Brushes.MediumAquamarine, Brushes.LightSteelBlue,
            Brushes.Salmon, Brushes.Gold, Brushes.LightPink, Brushes.MediumPurple
        };

        // Görselleştirme ayarı
        private double _diameterScale = 0.6; // px / dakika (kaydırıcı istersen ayrıca ekleriz)

        public MainWindow()
        {
            InitializeComponent();

            // DataGrid bağla
            dgVeri.ItemsSource = _records;
            dgVeri.AutoGenerateColumns = true;

            // Zamanlayıcı (hız slider’ına göre güncellenecek)
            _timer.Interval = TimeSpan.FromMilliseconds(600);
            _timer.Tick += (_, _) => AdvanceFrame();
            btnPlay.Click += (_, _) => Play();
            btnPause.Click += (_, _) => Pause();
            btnReset.Click += (_, _) => ResetSim();
            sldSpeed.ValueChanged += (_, _) =>
            {
                var spd = Math.Max(0.1, sldSpeed.Value);
                _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(80, 600 / spd));
            };


            // İstersen birkaç örnek kayıt (test için):
            SeedSample();
        }

            private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RebuildTimeline();
            DrawCirclesForCurrentFrame();
        }

        

        private void SeedSample()
        {
            var baseDate = new DateTime(2024, 9, 1, 13, 10, 0);
            _records.Add(new QueueRecord { PointId = "BN01", Timestamp = baseDate, DurationMin = 335 });
            _records.Add(new QueueRecord { PointId = "BN01", Timestamp = baseDate.AddMinutes(5), DurationMin = 185 });
            _records.Add(new QueueRecord { PointId = "BN01", Timestamp = baseDate.AddMinutes(10), DurationMin = 195 });
            _records.Add(new QueueRecord { PointId = "BN02", Timestamp = baseDate, DurationMin = 120 });
            _records.Add(new QueueRecord { PointId = "BN02", Timestamp = baseDate.AddMinutes(5), DurationMin = 200 });
            _records.Add(new QueueRecord { PointId = "BN03", Timestamp = baseDate, DurationMin = 60 });
            _records.Add(new QueueRecord { PointId = "BN03", Timestamp = baseDate.AddMinutes(10), DurationMin = 90 });
        }

        // === UI olayları ===
        private void Play()
        {
            if (_frames.Count == 0) RebuildTimeline();
            _timer.Start();
        }

        private void Pause() => _timer.Stop();

        private void ResetSim()
        {
            _timer.Stop();
            _frameIndex = 0;
            DrawCirclesForCurrentFrame();
        }

        // XAML’de tanımladığın Click bu imzayı çağırıyor
        private void btnEkle_Click(object sender, RoutedEventArgs e)
        {
            // Basit doğrulama
            var point = (txtBeklemeNoktasi.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(point))
            {
                MessageBox.Show("Waiting Point (BNxx) boş olamaz.");
                return;
            }
            if (dpTarih.SelectedDate == null)
            {
                MessageBox.Show("Date seçiniz.");
                return;
            }
            if (!TimeSpan.TryParse(txtSaat.Text.Trim(), out var ts))
            {
                MessageBox.Show("Time alanı HH:mm formatında olmalı (örn: 13:15).");
                return;
            }
            if (!int.TryParse(txtKuyrukSuresi.Text.Trim(), out var mins) || mins < 0)
            {
                MessageBox.Show("Queue Duration (min) 0 veya pozitif bir tam sayı olmalı.");
                return;
            }

            var when = dpTarih.SelectedDate.Value.Date + ts;


            // 5 dakika adımı kontrolü (ödevin tipik şartı)
            if ((when.Minute % 5) != 0)
            {
                if (MessageBox.Show("Saat, 5 dakikalık aralıklarda olmalı. Yine de ekleyeyim mi?",
                                    "Uyarı", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                    return;
            }

            if (when < DateTime.Now.AddYears(-1) || when > DateTime.Now.AddYears(1))
            {
                MessageBox.Show("Tarih makul bir aralıkta olmalı.");
                return;
            }

            _records.Add(new QueueRecord
            {
                PointId = point.ToUpperInvariant(),
                Timestamp = when,
                DurationMin = mins
            });

            // Zaman çizelgesini güncelle
            RebuildTimeline();
            DrawCirclesForCurrentFrame();
        }

        // Zaman çizelgesi ve simülasyon 
        private void RebuildTimeline()
        {
            _frames.Clear();
            _frames.AddRange(_records
                .Select(r => r.Timestamp)
                .Distinct()
                .OrderBy(t => t));

            _frameIndex = 0;

            // Palet güncelle
            _palette.Clear();
            int i = 0;
            foreach (var pid in _records.Select(r => r.PointId).Distinct().OrderBy(x => x))
                _palette[pid] = _colors[i++ % _colors.Length];
        }

        private void AdvanceFrame()
        {
            if (_frames.Count == 0) return;
            _frameIndex = (_frameIndex + 1) % _frames.Count;
            DrawCirclesForCurrentFrame();
        }

        private void DrawCirclesForCurrentFrame()
        {
            if (simCanvas.ActualWidth <= 0 || simCanvas.ActualHeight <= 0)
                return; // ölçüler hazır değilken çizme

            simCanvas.Children.Clear();
            if (_frames.Count == 0 && _records.Count == 0) return;

            DateTime now = _frames.Count > 0 ? _frames[_frameIndex] : DateTime.MinValue;

            // Her bekleme noktası için "o ana kadarki en güncel" süreyi al
            var snapshot = _records
                .Where(r => now == DateTime.MinValue || r.Timestamp <= now)
                .GroupBy(r => r.PointId)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(x => x.Timestamp).First();
                    return new { PointId = g.Key, Duration = latest.DurationMin };
                })
                .OrderBy(x => x.PointId)
                .ToList();

            // Daireleri yerleştir (sade ızgara yerleşimi)
            const double cellW = 160;
            const double cellH = 160;
            const double margin = 20;

            int colCount = Math.Max(1, (int)((simCanvas.ActualWidth - margin) / cellW));
            

            for (int idx = 0; idx < snapshot.Count; idx++)
            {
                var item = snapshot[idx];
                double diameter = Math.Max(24, item.Duration * _diameterScale); // alt limit 24 px

                int col = idx % colCount;
                int row = idx / colCount;

                double x = margin + col * cellW + (cellW - diameter) / 2;
                double y = margin + row * cellH + (cellH - diameter) / 2;

                var ellipse = new Ellipse
                {
                    Width = diameter,
                    Height = diameter,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Fill = _palette.TryGetValue(item.PointId, out var b) ? b : Brushes.LightGray
                };

                Canvas.SetLeft(ellipse, x);
                Canvas.SetTop(ellipse, y);
                simCanvas.Children.Add(ellipse);

                // Etiket (BN ve dakika)
                var label = new TextBlock
                {
                    Text = $"{item.PointId}\n{item.Duration} dk",
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(label, x + diameter / 2 - 40);
                Canvas.SetTop(label, y + diameter / 2 - 18);
                label.Width = 80;
                simCanvas.Children.Add(label);
            }

            
            if (_frames.Count > 0)
            {
                var tsBlock = new TextBlock
                {
                    Text = $"Zaman: {_frames[_frameIndex]:dd.MM.yyyy HH:mm}",
                    Margin = new Thickness(8),
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetLeft(tsBlock, 8);
                Canvas.SetTop(tsBlock, 8);
                simCanvas.Children.Add(tsBlock);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer.Stop(); // Pencere kapanırken zamanlayıcıyı durdur
        }

        private void SimCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (simCanvas.ActualWidth <= 0 || simCanvas.ActualHeight <= 0)
            {
                // Canvas henüz yüklenmemiş
                return;
            }

            DrawCirclesForCurrentFrame();
        }
    }
}
