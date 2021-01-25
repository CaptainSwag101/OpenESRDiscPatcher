using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using DiscUtils.Iso9660;
using System.ComponentModel;

namespace OpenESRDiscPatcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string DVD_VIDEO_DATA_FILENAME = "dvd_video_data.bin";
        private bool isBusy = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void PatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(InputIsoPath.Text))
            {
                MessageBox.Show("The specified input ISO does not exist!");
                return;
            }

            if (!File.Exists(DVD_VIDEO_DATA_FILENAME))
            {
                MessageBox.Show($"Unable to locate {DVD_VIDEO_DATA_FILENAME}! It should be located alongside the patcher program.");
                return;
            }

            if (File.Exists(OutputIsoPath.Text))
            {
                if (MessageBox.Show("The output ISO already exists! Overwrite?", string.Empty, MessageBoxButton.YesNo) == MessageBoxResult.No)
                    return;
            }


            using FileStream inputStream = new(InputIsoPath.Text, FileMode.Open, FileAccess.Read, FileShare.Read);
            using CDReader tempCDReader = new(inputStream, false);
            using BinaryReader tempReader = new(inputStream);

            // Check if the ISO has a UDF descriptor
            bool isUDF = false;
            for (int i = 1; i < 64; ++i)
            {
                long clusterOffset = tempCDReader.ClusterToOffset(i);
                inputStream.Seek(clusterOffset + short.MaxValue + 2, SeekOrigin.Begin);
                byte[] bufferMagic = tempReader.ReadBytes(3);

                string bufferMagicString = Encoding.ASCII.GetString(bufferMagic);
                if (bufferMagicString == "NSR")
                {
                    isUDF = true;
                    break;
                }
            }

            if (!isUDF)
            {
                MessageBox.Show("No UDF descriptor found!");
                return;
            }

            // Check if the ISO is already patched
            inputStream.Seek(tempCDReader.ClusterToOffset(14) + 25, SeekOrigin.Begin);
            string patchCheck = Encoding.ASCII.GetString(tempReader.ReadBytes(4));
            if (patchCheck == "+NSR")
            {
                MessageBox.Show("ISO has already been patched!");
                return;
            }

            // First, copy the input ISO to the output file
            isBusy = true;
            ProgressDialog progressDlg = new();
            progressDlg.Owner = this;
            progressDlg.Show();
            using FileStream outputStream = new(OutputIsoPath.Text, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            inputStream.Seek(0, SeekOrigin.Begin);
            await inputStream.CopyToAsync(outputStream);
            progressDlg.Close();

            // Then start processing the output ISO
            outputStream.Seek(0, SeekOrigin.Begin);
            using CDReader cdReader = new(outputStream, false);
            using BinaryReader isoReader = new(outputStream);
            using BinaryWriter isoWriter = new(outputStream);

            // Start the patching process:
            byte[] buffer;

            // Backup cluster 34 into cluster 14
            outputStream.Seek(cdReader.ClusterToOffset(34), SeekOrigin.Begin);
            buffer = isoReader.ReadBytes((int)cdReader.ClusterSize);
            outputStream.Seek(cdReader.ClusterToOffset(14), SeekOrigin.Begin);
            isoWriter.Write(buffer);

            // Backup cluster 50 into 15
            outputStream.Seek(cdReader.ClusterToOffset(50), SeekOrigin.Begin);
            buffer = isoReader.ReadBytes((int)cdReader.ClusterSize);
            outputStream.Seek(cdReader.ClusterToOffset(15), SeekOrigin.Begin);
            isoWriter.Write(buffer);

            // Modify cluster 34, make it point to a DVD_VIDEO structure
            outputStream.Seek(cdReader.ClusterToOffset(34), SeekOrigin.Begin);
            buffer = isoReader.ReadBytes((int)cdReader.ClusterSize);
            buffer[0xBC] = 0x80;
            buffer[0xBD] = 0x00;

            // Recalculate checksum
            ushort crcLength = BitConverter.ToUInt16(new byte[] { buffer[10], buffer[11] });
            ushort crc = CrcHelper.CalculateCRC(buffer, 16, crcLength);
            byte[] crcData = BitConverter.GetBytes(crc);
            buffer[8] = crcData[0];
            buffer[9] = crcData[1];
            byte tagChecksum = 0;
            for (int i = 0; i < 16; ++i)
            {
                // Skip byte 5 because it's the old checksum
                if (i == 4) continue;

                tagChecksum += buffer[i];
            }
            buffer[4] = tagChecksum;

            // Save the modified cluster 34
            outputStream.Seek(cdReader.ClusterToOffset(34), SeekOrigin.Begin);
            isoWriter.Write(buffer);

            // Modify cluster 50, make it point to a DVD_VIDEO structure
            outputStream.Seek(cdReader.ClusterToOffset(50), SeekOrigin.Begin);
            buffer = isoReader.ReadBytes((int)cdReader.ClusterSize);
            buffer[0xBC] = 0x80;
            buffer[0xBD] = 0x00;

            // Recalculate checksum
            crcLength = BitConverter.ToUInt16(new byte[] { buffer[10], buffer[11] });
            crc = CrcHelper.CalculateCRC(buffer, 16, crcLength);
            crcData = BitConverter.GetBytes(crc);
            buffer[8] = crcData[0];
            buffer[9] = crcData[1];
            tagChecksum = 0;
            for (int i = 0; i < 16; ++i)
            {
                // Skip byte 5 because it's the old checksum
                if (i == 4) continue;

                tagChecksum += buffer[i];
            }
            buffer[4] = tagChecksum;

            // Save the modified cluster 34
            outputStream.Seek(cdReader.ClusterToOffset(50), SeekOrigin.Begin);
            isoWriter.Write(buffer);

            // Write the DVD video data to cluster 128
            byte[] dvdVideoData = File.ReadAllBytes(DVD_VIDEO_DATA_FILENAME);
            outputStream.Seek(cdReader.ClusterToOffset(128), SeekOrigin.Begin);
            isoWriter.Write(dvdVideoData);

            isBusy = false;
            MessageBox.Show("Patched successfully!");
        }

        private async void UnpatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(InputIsoPath.Text))
            {
                MessageBox.Show("The specified input ISO does not exist!");
                return;
            }

            if (!File.Exists(DVD_VIDEO_DATA_FILENAME))
            {
                MessageBox.Show($"Unable to locate {DVD_VIDEO_DATA_FILENAME}! It should be located alongside the patcher program.");
                return;
            }

            // First, copy the input ISO to the output file
            if (File.Exists(OutputIsoPath.Text))
            {
                if (MessageBox.Show("The output ISO already exists! Overwrite?", string.Empty, MessageBoxButton.YesNo) == MessageBoxResult.No)
                    return;
            }

            using FileStream inputStream = new(InputIsoPath.Text, FileMode.Open, FileAccess.Read, FileShare.Read);
            using CDReader tempCDReader = new(inputStream, false);
            using BinaryReader tempReader = new(inputStream);

            // Check if the ISO has a UDF descriptor
            bool isUDF = false;
            for (int i = 1; i < 64; ++i)
            {
                long clusterOffset = tempCDReader.ClusterToOffset(i);
                inputStream.Seek(clusterOffset + short.MaxValue + 2, SeekOrigin.Begin);
                byte[] bufferMagic = tempReader.ReadBytes(3);

                string bufferMagicString = Encoding.ASCII.GetString(bufferMagic);
                if (bufferMagicString == "NSR")
                {
                    isUDF = true;
                    break;
                }
            }

            if (!isUDF)
            {
                MessageBox.Show("No UDF descriptor found!");
                return;
            }

            // Check if the ISO is already patched
            inputStream.Seek(tempCDReader.ClusterToOffset(14) + 25, SeekOrigin.Begin);
            string patchCheck = Encoding.ASCII.GetString(tempReader.ReadBytes(4));
            if (patchCheck != "+NSR")
            {
                MessageBox.Show("ISO was not already patched!");
                return;
            }

            // First, copy the input ISO to the output file
            isBusy = true;
            ProgressDialog progressDlg = new();
            progressDlg.Owner = this;
            progressDlg.Show();
            using FileStream outputStream = new(OutputIsoPath.Text, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            inputStream.Seek(0, SeekOrigin.Begin);
            await inputStream.CopyToAsync(outputStream);
            progressDlg.Close();

            // Then start processing the output ISO
            outputStream.Seek(0, SeekOrigin.Begin);
            using CDReader cdReader = new(outputStream, false);
            using BinaryReader isoReader = new(outputStream);
            using BinaryWriter isoWriter = new(outputStream);


            // Start the unpatching process:
            byte[] buffer;

            // Restore cluster 14 into cluster 34
            outputStream.Seek(cdReader.ClusterToOffset(14), SeekOrigin.Begin);
            buffer = isoReader.ReadBytes((int)cdReader.ClusterSize);
            outputStream.Seek(cdReader.ClusterToOffset(34), SeekOrigin.Begin);
            isoWriter.Write(buffer);

            // Restore cluster 15 into cluster 50
            outputStream.Seek(cdReader.ClusterToOffset(15), SeekOrigin.Begin);
            buffer = isoReader.ReadBytes((int)cdReader.ClusterSize);
            outputStream.Seek(cdReader.ClusterToOffset(50), SeekOrigin.Begin);
            isoWriter.Write(buffer);

            // Clear backup clusters and DVD_VIDEO data
            Array.Fill<byte>(buffer, 0);
            outputStream.Seek(cdReader.ClusterToOffset(14), SeekOrigin.Begin);
            isoWriter.Write(buffer);
            outputStream.Seek(cdReader.ClusterToOffset(15), SeekOrigin.Begin);
            isoWriter.Write(buffer);
            byte[] dvdvBuffer = new byte[new FileInfo(DVD_VIDEO_DATA_FILENAME).Length];
            outputStream.Seek(cdReader.ClusterToOffset(128), SeekOrigin.Begin);
            isoWriter.Write(dvdvBuffer);

            isBusy = false;
            MessageBox.Show("Unpatched successfully!");
        }

        private void BrowseInputFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new();
            dlg.Filter = "ISO disc image files (*.iso)|*.iso|All files (*.*)|*.*";

            // Abort if the user canceled the dialog
            if (!dlg.ShowDialog().Value)
            {
                return;
            }

            InputIsoPath.Text = dlg.FileName;

            // If there is nothing in the output file path textbox, populate it with an automatic output file in the same location
            if (string.IsNullOrWhiteSpace(OutputIsoPath.Text))
            {
                FileInfo temp = new(dlg.FileName);
                OutputIsoPath.Text = temp.FullName.Substring(0, temp.FullName.Length - temp.Extension.Length) + "_patched.iso";
            }
        }

        private void BrowseOutputFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new();
            dlg.Filter = "ISO disc image files (*.iso)|*.iso|All files (*.*)|*.*";

            // Abort if the user canceled the dialog
            if (!dlg.ShowDialog().Value)
            {
                return;
            }

            OutputIsoPath.Text = dlg.FileName;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (isBusy)
                e.Cancel = true;
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new();
            about.Owner = this;
            about.ShowDialog();
        }
    }
}
