using System;
using System.Collections.Generic;
using System.IO;
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
using System.Windows.Shapes;

namespace WPFTranslationApp
{
    /// <summary>
    /// Interaction logic for AboutScreen.xaml
    /// </summary>
    public partial class AboutScreen : Window
    {
        public AboutScreen()
        {
            InitializeComponent();
            String aboutFileName = "../../About Screen Text.txt";
            applyTextFileTextToTextBox(aboutFileName, AboutText);
        }



        private void applyTextFileTextToTextBox(String fileName, TextBox thisBox)
        {
            String thisBoxText = File.ReadAllText(fileName);
            thisBox.Text = thisBoxText;
        }


    }



}
