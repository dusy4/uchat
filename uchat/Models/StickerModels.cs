using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace uchat.Models
{
    public class StickerPackModel
    {
        public string Name { get; set; } = "";
        public string Cover { get; set; } = ""; 
        public Microsoft.UI.Xaml.Media.ImageSource? CoverImage { get; set; }
    }

    public class StickerItemModel : INotifyPropertyChanged
    {
        public string FileName { get; set; } = "";
        public string PackName { get; set; } = "";
        public string FullPath => $"{PackName}|{FileName}";
        private ImageSource? _image;
        public ImageSource? Image
        {
            get => _image;
            set
            {
                _image = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
