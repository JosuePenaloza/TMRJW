using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System;
// 🌟 CORRECCIÓN 1: Usamos un alias (WF) para Windows Forms 🌟
using WF = System.Windows.Forms;
using System.Linq;

namespace TMRJW
{
    public partial class ProyeccionWindow : Window
    {
        // 🌟 Elementos Visuales y Transformaciones 🌟
        private System.Windows.Controls.Image _imageDisplay;
        // 🌟 NUEVO: Para mostrar el texto del programa 🌟
        private TextBlock _textDisplay;

        private ScaleTransform _scaleTransform = new ScaleTransform();
        private TranslateTransform _translateTransform = new TranslateTransform();
        private Grid _layoutRoot;

        // 🌟 Variable de Point (Requiere ser explícito) 🌟
        private System.Windows.Point _lastMousePosition;

        private double _currentScale = 1.0;
        private const double MinScale = 0.1;
        private const double MaxScale = 5.0;
        private const double ScaleStep = 0.1;

        public ProyeccionWindow()
        {
            InitializeComponent();

            this.WindowStyle = WindowStyle.None;
            this.WindowState = WindowState.Maximized;
            this.Background = System.Windows.Media.Brushes.Black; // Especifica el namespace

            // 1. Crear el contenedor principal (_layoutRoot)
            _layoutRoot = new Grid();
            this.Content = _layoutRoot;

            // 2. Crear el Contenedor de Transformación para el Zoom/Pan
            TransformGroup transformGroup = new TransformGroup();
            transformGroup.Children.Add(_scaleTransform);
            transformGroup.Children.Add(_translateTransform);

            // 3. Crear y configurar el elemento Image 
            _imageDisplay = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                RenderTransform = transformGroup,
                Visibility = Visibility.Visible // Visible por defecto
            };
            _layoutRoot.Children.Add(_imageDisplay);

            // 🌟 NUEVO: Crear y configurar el TextBlock para el programa 🌟
            _textDisplay = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White, // Especifica el namespace
                FontSize = 48,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, // Usa el tipo
                VerticalAlignment = System.Windows.VerticalAlignment.Center,     // Usa el tipo
                Visibility = Visibility.Collapsed // Oculto por defecto
            };
            _layoutRoot.Children.Add(_textDisplay);

            // 4. Asignar los eventos de Mouse 
            _layoutRoot.MouseWheel += LayoutRoot_MouseWheel;
            _layoutRoot.MouseDown += LayoutRoot_MouseDown;
            _layoutRoot.MouseMove += LayoutRoot_MouseMove;
            _layoutRoot.MouseUp += LayoutRoot_MouseUp;
        }

        // 🌟 MÉTODO: Coloca la ventana en el monitor de salida correcto
        public void ActualizarMonitor(int monitorIndex)
        {
            // 🌟 CORRECCIÓN 2: Usamos el alias WF 🌟
            WF.Screen[] screens = WF.Screen.AllScreens;

            if (monitorIndex >= 0 && monitorIndex < screens.Length)
            {
                WF.Screen targetScreen = screens[monitorIndex];

                this.WindowState = WindowState.Normal;
                this.Left = targetScreen.Bounds.X;
                this.Top = targetScreen.Bounds.Y;
                this.Width = targetScreen.Bounds.Width;
                this.Height = targetScreen.Bounds.Height;
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;
            }
            else
            {
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;
            }

        // Método llamado por MainWindow para mostrar la imagen del Texto del Año
        public void MostrarImagenTexto(BitmapImage imagen)
        {
            _textDisplay.Visibility = Visibility.Collapsed; // Oculta el texto
            _imageDisplay.Visibility = Visibility.Visible;  // Muestra la imagen

            _imageDisplay.Source = imagen;

            // Reinicia la posición y el zoom
            _currentScale = 1.0;
            _scaleTransform.ScaleX = _currentScale;
            _scaleTransform.ScaleY = _currentScale;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;
        }

        // 🌟 NUEVO MÉTODO: Muestra el contenido del programa semanal 🌟
        public void MostrarTextoPrograma(string contenido)
        {
            _imageDisplay.Visibility = Visibility.Collapsed; // Oculta la imagen
            _textDisplay.Visibility = Visibility.Visible;    // Muestra el texto
            _textDisplay.Text = contenido;

            // Reinicia la posición y el zoom, aunque no son necesarios para el TextBlock
            _currentScale = 1.0;
            _scaleTransform.ScaleX = _currentScale;
            _scaleTransform.ScaleY = _currentScale;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;
        }

        // --- Manejo de Eventos para Zoom y Pan ---
        private void LayoutRoot_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.Space))
            {
                System.Windows.Point mousePosition = e.GetPosition(_imageDisplay);
                double scaleFactor = (e.Delta > 0) ? (1.0 + ScaleStep) : (1.0 - ScaleStep);

                double newScale = _currentScale * scaleFactor;

                if (newScale < MinScale) newScale = MinScale;
                if (newScale > MaxScale) newScale = MaxScale;

                _scaleTransform.ScaleX = newScale;
                _scaleTransform.ScaleY = newScale;

                _translateTransform.X = mousePosition.X - (mousePosition.X * (newScale / _currentScale));
                _translateTransform.Y = mousePosition.Y - (mousePosition.Y * (newScale / _currentScale));

                _currentScale = newScale;
            }
        }

        private void LayoutRoot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _lastMousePosition = e.GetPosition(_layoutRoot);
                _layoutRoot.CaptureMouse();
            }
        }

        private void LayoutRoot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_layoutRoot.IsMouseCaptured)
            {
                System.Windows.Point currentMousePosition = e.GetPosition(_layoutRoot);
                double deltaX = currentMousePosition.X - _lastMousePosition.X;
                double deltaY = currentMousePosition.Y - _lastMousePosition.Y;

                _translateTransform.X += deltaX;
                _translateTransform.Y += deltaY;

                _lastMousePosition = currentMousePosition;
            }
        }

        private void LayoutRoot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _layoutRoot.ReleaseMouseCapture();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Este método permanece vacío.
        }
    }
}