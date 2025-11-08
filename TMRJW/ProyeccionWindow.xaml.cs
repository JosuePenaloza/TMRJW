using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TMRJW
{
    public partial class ProyeccionWindow : Window
    {
        private Image _front => _frontIsA ? ProjectionImageA : ProjectionImageB;
        private Image _back => _frontIsA ? ProjectionImageB : ProjectionImageA;
        private bool _frontIsA = true;
        private readonly TimeSpan _fadeDuration = TimeSpan.FromMilliseconds(180);

        private bool _isPlayingVideo = false;
        private string? _currentVideoPath = null;

        // Timer para actualizar progreso de reproducción
        private DispatcherTimer? _playbackTimer;

        // Eventos públicos para que MainWindow pueda escuchar progreso/fin
        public event Action<TimeSpan, TimeSpan?>? PlaybackProgress;
        public event Action? PlaybackEnded;

        // Indica si la última imagen mostrada fue el Texto del Año para ajustar la transición
        private bool _lastWasTextoAnio = false;

        public ProyeccionWindow()
        {
            InitializeComponent();

            // Mejorar rendimiento y reducir parpadeos
            try
            {
                ProjectionImageA.CacheMode = new BitmapCache();
                ProjectionImageB.CacheMode = new BitmapCache();
                ProjectionImageA.SnapsToDevicePixels = true;
                ProjectionImageB.SnapsToDevicePixels = true;
                UseLayoutRounding = true;

                RenderOptions.SetBitmapScalingMode(ProjectionImageA, BitmapScalingMode.HighQuality);
                RenderOptions.SetBitmapScalingMode(ProjectionImageB, BitmapScalingMode.HighQuality);
            }
            catch { }

            // Configurar timer (cada250ms)
            _playbackTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _playbackTimer.Tick += (s, e) =>
            {
                try
                {
                    if (ProjectionMedia != null && ProjectionMedia.NaturalDuration.HasTimeSpan)
                    {
                        var pos = ProjectionMedia.Position;
                        var dur = ProjectionMedia.NaturalDuration.HasTimeSpan ? ProjectionMedia.NaturalDuration.TimeSpan : (TimeSpan?)null;
                        PlaybackProgress?.Invoke(pos, dur);
                    }
                    else if (ProjectionMedia != null)
                    {
                        var pos = ProjectionMedia.Position;
                        PlaybackProgress?.Invoke(pos, null);
                    }
                }
                catch { }
            };

            // Manejar evento fin de media
            try
            {
                ProjectionMedia.MediaEnded += (s, e) =>
                {
                    try
                    {
                        _isPlayingVideo = false;
                        _playbackTimer?.Stop();
                        PlaybackEnded?.Invoke();
                        // parar y ocultar media, restaurar imágenes
                        ProjectionMedia.Stop();
                        ProjectionMedia.Visibility = Visibility.Collapsed;
                        ProjectionImageA.Visibility = Visibility.Visible;
                        ProjectionImageB.Visibility = Visibility.Visible;

                        // clear current path so subsequent PlayVideo will open anew
                        _currentVideoPath = null;
                    }
                    catch { }
                };
            }
            catch { }
        }

        // Cross-fade sin dejar frame en negro; acepta BitmapImage ya congelado (Freeze).
        public void MostrarImagenTexto(BitmapImage imagen)
        {
            if (imagen == null) return;

            // Asegurar que la imagen está congelada para evitar problemas de rendering
            try
            {
                if (imagen.CanFreeze && !imagen.IsFrozen)
                    imagen.Freeze();
            }
            catch { }

            // Si hay vídeo en reproducción, detenerlo y ocultar media
            try { StopVideo(); } catch { }

            // Asegurarse de ejecutar en el hilo de UI
            Dispatcher.BeginInvoke((Action)(() =>
            {
                try
                {
                    // Cancelar cualquier animación previa para evitar estados inconsistentes
                    try
                    {
                        _front.BeginAnimation(UIElement.OpacityProperty, null);
                        _back.BeginAnimation(UIElement.OpacityProperty, null);
                    }
                    catch { }

                    // Asegurar que ambas imágenes están visibles para el cross-fade
                    ProjectionImageA.Visibility = Visibility.Visible;
                    ProjectionImageB.Visibility = Visibility.Visible;

                    // Si la última imagen mostrada fue Texto del Año, evitar animación para prevenir flash
                    if (_lastWasTextoAnio)
                    {
                        try
                        {
                            // asignación directa sin animación
                            _front.BeginAnimation(UIElement.OpacityProperty, null);
                            _back.BeginAnimation(UIElement.OpacityProperty, null);
                            _front.Source = imagen;
                            _front.Opacity = 1.0;
                            _back.Source = null;
                            _back.Opacity = 0.0;
                            _lastWasTextoAnio = false;
                            return;
                        }
                        catch { /* si falla, continuar con el flujo normal */ }
                    }

                    // Si es la primera imagen (front no tiene Source) simplemente asignarla sin animación
                    if (_front.Source == null && (_back.Source == null))
                    {
                        _front.Source = imagen;
                        _front.Opacity = 1.0;
                        _back.Source = null;
                        _back.Opacity = 0.0;
                        return;
                    }

                    // Si la imagen a mostrar ya está en front/back, evitar recrear animación innecesaria
                    if (ReferenceEquals(_front.Source, imagen) || ReferenceEquals(_back.Source, imagen))
                    {
                        // Si ya está en back pero no visible, forzar swap inmediato si es necesario
                        if (ReferenceEquals(_back.Source, imagen) && _back.Opacity < 0.5)
                        {
                            _back.Opacity = 1.0;
                            _front.Opacity = 0.0;
                            _frontIsA = !_frontIsA;
                        }
                        return;
                    }

                    // Asegurar z-order: colocar el buffer trasero por encima durante la transición
                    try { Panel.SetZIndex(_back, 1); Panel.SetZIndex(_front, 0); } catch { }

                    // Colocar la nueva imagen en el buffer trasero
                    _back.Source = imagen;
                    _back.Opacity = 0.0;
                    ProjectionMedia.Visibility = Visibility.Collapsed;

                    // Forzar layout/render antes de empezar la animación para que el back tenga su frame listo
                    try
                    {
                        // Solicitar render immediato
                        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                        _back.UpdateLayout();
                        _front.UpdateLayout();
                    }
                    catch { }

                    // Forzar valores iniciales (por si el sistema los dejó entremedias)
                    _back.Visibility = Visibility.Visible;
                    _front.Visibility = Visibility.Visible;
                    _front.Opacity = 1.0;

                    // Preparar animación: front opa 1 -> 0, back opa 0 -> 1
                    var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(_fadeDuration)) { FillBehavior = FillBehavior.HoldEnd };
                    var fadeIn  = new DoubleAnimation(0.0, 1.0, new Duration(_fadeDuration)) { FillBehavior = FillBehavior.HoldEnd };

                    // Aplicar al back (subirá) y front (bajará)
                    _back.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    _front.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                    // Al finalizar la animación, normalizar y alternar referencias
                    fadeIn.Completed += (s, e) =>
                    {
                        try
                        {
                            // Quitar animaciones
                            _front.BeginAnimation(UIElement.OpacityProperty, null);
                            _back.BeginAnimation(UIElement.OpacityProperty, null);

                            // Establecer opacidades finales explícitamente
                            _back.Opacity = 1.0;
                            _front.Opacity = 0.0;

                            // Alternar buffers: el que antes era back ahora será front lógicamente
                            _frontIsA = !_frontIsA;

                            // Asegurar que el visible (nuevo front) tenga opacidad1
                            _front.Opacity = 1.0;
                            _back.Opacity = 0.0;

                            // Reset z-order
                            try { Panel.SetZIndex(_front, 1); Panel.SetZIndex(_back, 0); } catch { }

                            // No borrar Source para evitar frames en negro; mantener en memoria
                        }
                        catch
                        {
                            // Fallback simple: asignación directa sin animación si algo falla
                            try { _front.BeginAnimation(UIElement.OpacityProperty, null); _back.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
                            _front.Source = imagen;
                            _front.Opacity = 1.0;
                            _back.Opacity = 0.0;
                        }
                    };
                }
                catch
                {
                    // Fallback simple: asignación directa sin animación si algo falla
                    try { _front.BeginAnimation(UIElement.OpacityProperty, null); _back.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
                    _front.Source = imagen;
                    _front.Opacity = 1.0;
                    _back.Source = null;
                    _back.Opacity = 0.0;
                }
            }), DispatcherPriority.Normal);
        }

        // Actualiza transform (escala + traslación) aplicados a imágenes y media (para sincronizar zoom/pan)
        // Firma existente que acepta escala X/Y y traslación X/Y
        public void UpdateImageTransform(double scaleX = 1.0, double scaleY = 1.0, double translateX = 0.0, double translateY = 0.0)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                var tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(scaleX, scaleY));
                tg.Children.Add(new TranslateTransform(translateX, translateY));
                ApplyRenderTransformToVisuals(tg);
            }), DispatcherPriority.Normal);
        }

        // Nueva sobrecarga para compatibilidad con llamadas existentes que pasan (scale, tx, ty)
        public void UpdateImageTransform(double scale, double translateX, double translateY)
        {
            UpdateImageTransform(scale, scale, translateX, translateY);
        }

        // Sobrecarga que acepta transforms ya construidos
        public void UpdateImageTransform(ScaleTransform scaleTransform, TranslateTransform translateTransform)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                var tg = new TransformGroup();
                tg.Children.Add(scaleTransform ?? new ScaleTransform(1,1));
                tg.Children.Add(translateTransform ?? new TranslateTransform(0,0));
                ApplyRenderTransformToVisuals(tg);
            }), DispatcherPriority.Normal);
        }

        private void ApplyRenderTransformToVisuals(Transform transform)
        {
            try
            {
                ProjectionImageA.RenderTransformOrigin = new Point(0.5,0.5);
                ProjectionImageB.RenderTransformOrigin = new Point(0.5,0.5);
                ProjectionMedia.RenderTransformOrigin = new Point(0.5,0.5);

                ProjectionImageA.RenderTransform = transform;
                ProjectionImageB.RenderTransform = transform;
                ProjectionMedia.RenderTransform = transform;
            }
            catch { }
        }

        // ------------------ Video control helpers (API simple usada por MainWindow) ------------------

        public void PlayVideo(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            try
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        // Stop any preview audio in BrowserWindow instances to avoid audio overlap.
                        // Use reflection to invoke StopPreviewPlaybackAndReset if present (may be non-public) so we don't create a hard dependency.
                        try
                        {
                            foreach (Window w in Application.Current.Windows)
                            {
                                try
                                {
                                    var t = w.GetType();
                                    var mi = t.GetMethod("StopPreviewPlaybackAndReset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                    if (mi != null)
                                    {
                                        try
                                        {
                                            if (w.Dispatcher != null)
                                                w.Dispatcher.Invoke(() => { try { mi.Invoke(w, null); } catch { } });
                                            else
                                                mi.Invoke(w, null);
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }

                        // If same video is loaded and currently paused, resume instead of reloading
                        if (!string.IsNullOrWhiteSpace(_currentVideoPath) && string.Equals(_currentVideoPath, filePath, StringComparison.OrdinalIgnoreCase)
 && ProjectionMedia.Source != null && !_isPlayingVideo)
 {
 ProjectionMedia.Play();
 _isPlayingVideo = true;
 try { _playbackTimer?.Start(); } catch { }
 return;
 }

 // Otherwise load new source
 ProjectionMedia.Source = new Uri(filePath);
 ProjectionMedia.Position = TimeSpan.Zero;
 ProjectionMedia.Visibility = Visibility.Visible;
 ProjectionImageA.Visibility = Visibility.Collapsed;
 ProjectionImageB.Visibility = Visibility.Collapsed;
 ProjectionMedia.Play();
 _isPlayingVideo = true;
 _currentVideoPath = filePath;

 try { _playbackTimer?.Start(); } catch { }
 }
 catch
 {
 _isPlayingVideo = false;
 }
 }), DispatcherPriority.Normal);
 }
 catch { }
 }

 public void PlayVideo(Uri source)
 {
 if (source == null) return;
 PlayVideo(source.OriginalString);
 }

 public void PauseVideo()
 {
 Dispatcher.BeginInvoke((Action)(() =>
 {
 try
 {
 // Pause playback but keep the Media element visible so the paused frame remains shown
 ProjectionMedia.Pause();
 _isPlayingVideo = false;
 try { _playbackTimer?.Stop(); } catch { }
 // Do not change ProjectionMedia.Visibility or image visibility here;
 // keeping the Media visible preserves the paused frame for inspection.
 }
 catch { }
 }), DispatcherPriority.Normal);
 }

 // Try to resume playback without reloading the source. Returns true if resumed.
 public bool TryResume()
 {
 try
 {
 if (ProjectionMedia == null) return false;
 // If a source is already loaded and we're not playing, resume
 if (ProjectionMedia.Source != null && !_isPlayingVideo)
 {
 Dispatcher.BeginInvoke((Action)(() =>
 {
 try
 {
 // Mostrar media y ocultar imágenes
 try { ProjectionImageA.Visibility = Visibility.Collapsed; ProjectionImageB.Visibility = Visibility.Collapsed; } catch { }
 try { ProjectionMedia.Visibility = Visibility.Visible; } catch { }
 ProjectionMedia.Play(); _isPlayingVideo = true; try { _playbackTimer?.Start(); } catch { }
 }
 catch { }
 }), DispatcherPriority.Normal);
 return true;
 }
 }
 catch { }
 return false;
 }

 public void StopVideo()
 {
 Dispatcher.BeginInvoke((Action)(() =>
 {
 try
 {
 ProjectionMedia.Stop();
 ProjectionMedia.Source = null;
 ProjectionMedia.Visibility = Visibility.Collapsed;
 _isPlayingVideo = false;
 try { _playbackTimer?.Stop(); } catch { }
 // Restaurar imágenes visibles para evitar pantalla en negro
 ProjectionImageA.Visibility = Visibility.Visible;
 ProjectionImageB.Visibility = Visibility.Visible;

 _currentVideoPath = null;
 }
 catch { }
 }), DispatcherPriority.Normal);
 }

 public void SetVolume(double level)
 {
 Dispatcher.BeginInvoke((Action)(() =>
 {
 try
 {
 var v = Math.Max(0.0, Math.Min(1.0, level));
 ProjectionMedia.Volume = v;
 }
 catch { }
 }), DispatcherPriority.Normal);
 }

 public bool IsPlayingVideo()
 {
 return _isPlayingVideo;
 }

        // Expose current source string (if any)
        public string? CurrentVideoSource => ProjectionMedia?.Source?.OriginalString;

        // Permite buscar a una fracción (0..1) del video
        public void SeekToFraction(double fraction)
        {
            try
            {
                if (ProjectionMedia == null) return;
                if (!ProjectionMedia.NaturalDuration.HasTimeSpan) return;
                var dur = ProjectionMedia.NaturalDuration.TimeSpan;
                if (dur.TotalSeconds <= 0) return;
                var pos = TimeSpan.FromSeconds(Math.Max(0.0, Math.Min(1.0, fraction)) * dur.TotalSeconds);
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        ProjectionMedia.Position = pos;
                    }
                    catch { }
                }), DispatcherPriority.Normal);
            }
            catch { }
        }

        // Obtiene la duración del video si está disponible
        public TimeSpan? GetVideoDuration()
        {
            try
            {
                if (ProjectionMedia == null) return null;
                return ProjectionMedia.NaturalDuration.HasTimeSpan ? ProjectionMedia.NaturalDuration.TimeSpan : (TimeSpan?)null;
            }
            catch { return null; }
        }

        public void SetLastWasTextoAnio(bool v)
        {
            _lastWasTextoAnio = v;
        }
    }
}
