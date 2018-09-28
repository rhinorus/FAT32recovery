using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilesUndelitingFAT32
{
    class DriveFile
    {
        private string name;
        public string Name
        {
            get { return name; }

            set //очищаем имя от лишних Юникодовских символов.
            {
                StringBuilder sb = new StringBuilder();

                if (value.Contains('\0'))
                    sb.Append(value.Split('\0')[0]);
                else
                    sb.Append(value);

                while (sb[sb.Length - 1] == ' ')
                    sb.Remove(sb.Length - 1, 1);

                name = sb.ToString();
            }
        }
        public List<int> Clasters { get; set; }

        public DriveFile()
        {
            Clasters = new List<int>();
            Name = "no-name";
        }

        public DriveFile(string name = "no-name")
        {
            Clasters = new List<int>();
            Name = name;
        }
    }
}
