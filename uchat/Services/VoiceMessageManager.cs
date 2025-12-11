using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace uchat.Services
{
    public class VoiceMessageManager
    {
        private MediaCapture? _mediaCapture;
        private InMemoryRandomAccessStream? _audioStream;
        private bool _isRecording;

        public bool IsRecording => _isRecording;

        public async Task InitializeAsync()
        {
            if (_mediaCapture != null) return;

            _mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio
            };

            try
            {
                await _mediaCapture.InitializeAsync(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Microphone initialization failed: {ex.Message}");
                _mediaCapture = null;
                throw; // Let the UI handle the error
            }
        }

        public async Task StartRecordingAsync()
        {
            if (_mediaCapture == null) await InitializeAsync();

            if (_isRecording) return;

            try
            {
                _audioStream = new InMemoryRandomAccessStream();
                // Record in MP3 format if possible, or M4A for better compatibility
                var encodingProfile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.Medium);

                await _mediaCapture!.StartRecordToStreamAsync(encodingProfile, _audioStream);
                _isRecording = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Start recording failed: {ex.Message}");
                _isRecording = false;
            }
        }

        public async Task<string?> StopRecordingAsync()
        {
            if (!_isRecording || _mediaCapture == null || _audioStream == null) return null;

            try
            {
                await _mediaCapture.StopRecordAsync();
                _isRecording = false;

                // Convert stream to Base64 string
                _audioStream.Seek(0);
                using (var dataReader = new DataReader(_audioStream.GetInputStreamAt(0)))
                {
                    await dataReader.LoadAsync((uint)_audioStream.Size);
                    var bytes = new byte[_audioStream.Size];
                    dataReader.ReadBytes(bytes);
                    return Convert.ToBase64String(bytes);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stop recording failed: {ex.Message}");
                return null;
            }
            finally
            {
                _audioStream?.Dispose();
                _audioStream = null;
            }
        }

        public void Dispose()
        {
            _mediaCapture?.Dispose();
            _mediaCapture = null;
            _audioStream?.Dispose();
            _audioStream = null;
        }
    }
}