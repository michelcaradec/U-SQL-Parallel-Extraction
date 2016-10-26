using Microsoft.Analytics.Interfaces;
using Microsoft.Analytics.Types.Sql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ParallelExtraction
{
    [SqlUserDefinedExtractor]
    public class PartitionExtractor : IExtractor
    {
        private string _cnxString;
        private long _id = (DateTime.UtcNow - DateTime.UtcNow.Date).Ticks;

        public PartitionExtractor(string cnxString)
        {
            _cnxString = cnxString;
        }

        public override IEnumerable<IRow> Extract(IUnstructuredReader input, IUpdatableRow output)
        {
            string id;
            string from;
            string to;

            // 1. Collect partition informations.
            using (var reader = new StreamReader(input.BaseStream))
            {
                string line = reader.ReadLine();
                var parts = line.Split('\t');
                id = parts[0];
                from = parts[1];
                to = parts[2];
            }

            // 2. Read data source using partition information.
            using (var reader = ProviderFactory.CreateInstance(_cnxString, from, to))
            {
                foreach (var row in reader.Rows)
                {
                    output.Set("extractor_id", _id);
                    output.Set("partition_id", id);
                    output.Set("partition", row[0]);
                    output.Set("value1", row[1]);
                    output.Set("value2", row[2]);

                    yield return output.AsReadOnly();
                }
            }

			// Add some latency to data read.
            Thread.Sleep(10000);
        }
    }

    #region Factory

    internal class Connection
    {
        public string ConnectionString { get; private set; }
        public ProviderType Provider { get; set; }
        public string DataSource { get; private set; }

        public Connection(string cnx)
        {
            ConnectionString = cnx;
            Parse(ConnectionString);
        }

        private void Parse(string cnx)
        {
            var properties = cnx.Split(';').Select(it => it.Split('=')).ToDictionary(k => k[0], v => v[1], StringComparer.OrdinalIgnoreCase);
            Provider = (ProviderType)Enum.Parse(typeof(ProviderType), properties["provider"], true);
            DataSource = properties["datasource"];
        }
    }

    internal static class ProviderFactory
    {
        public static IProvider CreateInstance(string cnxString, string from, string to)
        {
            var cnx = new Connection(cnxString);
            switch (cnx.Provider)
            {
                case ProviderType.SampleProvider:
                    return new SampleProvider(cnx, from, to);

                default:
                    throw new NotSupportedException();
            }
        }
    }

    #endregion
    #region Providers

    internal enum ProviderType { SampleProvider }

    internal interface IProvider : IDisposable
    {
        IEnumerable<string[]> Rows { get; }
    }

    internal class SampleProvider : IProvider
    {
        private Connection _cnx;
        private string _from;
        private string _to;

        public SampleProvider(Connection cnx, string from, string to)
        {
            _cnx = cnx;
            _from = from;
            _to = to;
        }

        #region IProvider

        public IEnumerable<string[]> Rows
        {
            get
            {
                using (var stream = File.OpenRead(Environment.ExpandEnvironmentVariables(_cnx.DataSource)))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var tokens = line.Split('\t');
                            string pk = tokens[0];
                            if (pk.CompareTo(_from) >= 0 && pk.CompareTo(_to) <= 0)
                            {
                                yield return tokens;
                            }
                        }
                    }
                }
            }
        }

        #endregion
        #region IDisposable

        public void Dispose()
        {
        }

        #endregion
    }

    #endregion
}
