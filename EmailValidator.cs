using System;
using System.Linq;
using System.Net.Mail;
using DnsClient;
using DnsClient.Protocol;

namespace EmailValidator
{
    public class EmailValidator
    {
        public bool Validate(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            email = email.Trim();

            MailAddress mailAddress = null;

            try
            {
                mailAddress = new MailAddress(email);
            }
            catch (Exception e)
            { return false; }

            if (mailAddress.Address != email)
            {
                result = EmailValidationResult.InvalidFormat;
                return true;
            }

            //////////////////

            LookupClient dnsClient = new LookupClient() { UseTcpOnly = true };

            var mxRecords = dnsClient.Query(mailAddress.Host, QueryType.MX).AllRecords.MxRecords().ToList();

            if (mxRecords.Count == 0)
            {
                return false; // No MX Records
            }

            foreach (MxRecord mxRecord in mxRecords)
            {
                try
                {
                    SmtpClient smtpClient = new SmtpClient(mxRecord.Exchange.Value);
                    SmtpStatusCode resultCode;

                    if (smtpClient.CheckMailboxExists(email, out resultCode))
                    {
                        switch (resultCode)
                        {
                            case SmtpStatusCode.Ok:
                                // OK
                                return true;

                            case SmtpStatusCode.ExceededStorageAllocation:
                                // Mailbox Storage Exceeded
                                return true;

                            case SmtpStatusCode.MailboxUnavailable:
                                // Mailbox Unavailable
                                return true;
                        }
                    }
                }
                catch (SmtpClientException)
                {
                }
                catch (ArgumentNullException)
                {
                }
            }

            if (mxRecords.Count > 0)
            {
                // Mail Server Unavailable
                return false;
            }

            // Undefined
            return false;
        }
    }

    
    internal class SmtpClient
    {
        private struct SmtpResponse
        {
            public string Raw { get; set; }
            public SmtpStatusCode Code { get; set; }
        }

        private readonly string host;
        private readonly int port;

        public SmtpClient(string host, int port = 25)
        {
            this.host = host;
            this.port = port;
        }

        public bool CheckMailboxExists(string email, out SmtpStatusCode result)
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    tcpClient.SendTimeout = 1000;
                    tcpClient.ReceiveTimeout = 1000;

                    if (!tcpClient.ConnectAsync(this.host, this.port).Wait(1000))
                    {
                        throw new SmtpClientTimeoutException();
                    }

                    NetworkStream networkStream = tcpClient.GetStream();
                    StreamReader streamReader = new StreamReader(networkStream);

                    this.AcceptResponse(streamReader, SmtpStatusCode.ServiceReady);

                    string mailHost = (new MailAddress(email)).Host;

                    this.SendCommand(networkStream, streamReader, "HELO " + mailHost, SmtpStatusCode.Ok);
                    this.SendCommand(networkStream, streamReader, "MAIL FROM:<check@" + mailHost + ">", SmtpStatusCode.Ok);
                    SmtpResponse response = this.SendCommand(networkStream, streamReader, "RCPT TO:<" + email + ">");
                    this.SendCommand(networkStream, streamReader, "QUIT", SmtpStatusCode.ServiceClosingTransmissionChannel, SmtpStatusCode.MailboxUnavailable);

                    result = response.Code;

                    return true;
                }
            }
            catch (IOException e)
            {
                // StreamReader problem
            }
            catch (SocketException e)
            {
                // TcpClient problem
            }

            result = SmtpStatusCode.GeneralFailure;
            return false;
        }

        private SmtpResponse SendCommand(NetworkStream networkStream, StreamReader streamReader, string command, params SmtpStatusCode[] goodReplys)
        {
            var dataBuffer = Encoding.ASCII.GetBytes(command + "\r\n");
            networkStream.Write(dataBuffer, 0, dataBuffer.Length);

            return this.AcceptResponse(streamReader, goodReplys);
        }

        private SmtpResponse AcceptResponse(StreamReader streamReader, params SmtpStatusCode[] goodReplys)
        {
            string response = streamReader.ReadLine();

            if (string.IsNullOrEmpty(response) || response.Length < 3)
            {
                throw new SmtpClientException("Invalid response");
            }

            SmtpStatusCode smtpStatusCode = this.GetResponseCode(response);

            if (goodReplys.Length > 0 && !goodReplys.Contains(smtpStatusCode))
            {
                throw new SmtpClientException(response);
            }

            return new SmtpResponse
            {
                Raw = response,
                Code = smtpStatusCode
            };
        }

        private SmtpStatusCode GetResponseCode(string response)
        {
            return (SmtpStatusCode) Enum.Parse(typeof(SmtpStatusCode), response.Substring(0, 3));
        }
    }
}
