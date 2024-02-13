using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace WatsonWebsocket
{
    /// <summary>
    /// WatsonWebsocket statistics.
    /// </summary>
    public class Statistics
    {
        /// <summary>
        /// The time at which the client or server was started.
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now.ToUniversalTime();

        /// <summary>
        /// The amount of time which the client or server has been up.
        /// </summary>
        public TimeSpan UpTime => DateTime.Now.ToUniversalTime() - StartTime;

        /// <summary>
        /// The number of bytes received.
        /// </summary>
        public long ReceivedBytes;

        /// <summary>
        /// The number of messages received.
        /// </summary>
        public long ReceivedMessages;

        /// <summary>
        /// Average received message size in bytes.
        /// </summary>
        public int ReceivedMessageSizeAverage
        {
            get
            {
                if (ReceivedBytes > 0 && ReceivedMessages > 0)
                {
                    return (int)(ReceivedBytes / ReceivedMessages);
                }

                return 0;
            }
        }

        /// <summary>
        /// The number of bytes sent.
        /// </summary>
        public long SentBytes;

        /// <summary>
        /// The number of messages sent.
        /// </summary>
        public long SentMessages;


        /// <summary>
        /// Average sent message size in bytes.
        /// </summary>
        public decimal SentMessageSizeAverage
        {
            get
            {
                if (SentBytes > 0 && SentMessages > 0)
                {
                    return (int)(SentBytes / SentMessages);
                }

                return 0;
            }
        }

        /// <summary>
        /// Initialize the statistics object.
        /// </summary>
        public Statistics()
        {

        }

        /// <summary>
        /// Return human-readable version of the object.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine("--- Statistics ---");
            builder.Append("    Started     : ")
                .Append(StartTime)
                .AppendLine();
            builder.Append("    Uptime      : ")
                .Append(UpTime)
                .AppendLine();
            builder.AppendLine("    Received    : ");
            builder.Append("       Bytes    : ")
                .Append(ReceivedBytes)
                .AppendLine();
            builder.Append( "       Messages : ")
                .Append(ReceivedMessages)
                .AppendLine();
            builder.Append("       Average  : ")
                .Append(ReceivedMessageSizeAverage)
                .Append(" bytes")
                .AppendLine();
            builder.AppendLine("    Sent        : ");
            builder.Append("       Bytes    : ")
                .Append(SentBytes)
                .AppendLine();
            builder.Append("       Messages : ")
                .Append(SentMessages)
                .AppendLine();
            builder.Append("       Average  : ")
                .Append(SentMessageSizeAverage)
                .Append(" bytes")
                .AppendLine();

            return builder.ToString();
        }

        /// <summary>
        /// Reset statistics other than StartTime and UpTime.
        /// </summary>
        public void Reset()
        {
            ReceivedBytes = 0;
            ReceivedMessages = 0;
            SentBytes = 0;
            SentMessages = 0;
        }

        internal void IncrementReceivedMessages()
        {
            ReceivedMessages = Interlocked.Increment(ref ReceivedMessages);
        }

        internal void IncrementSentMessages()
        {
            SentMessages = Interlocked.Increment(ref SentMessages);
        }

        internal void AddReceivedBytes(long bytes)
        {
            ReceivedBytes = Interlocked.Add(ref ReceivedBytes, bytes);
        }

        internal void AddSentBytes(long bytes)
        {
            SentBytes = Interlocked.Add(ref SentBytes, bytes);
        }
    }
}
