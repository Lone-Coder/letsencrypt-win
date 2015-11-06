﻿using LetsEncrypt.ACME.JOSE;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.Administration;

namespace LetsEncrypt.ACME.CLI
{
    class Program
    {
        public static string BaseURI { get; set; } = "https://acme-staging.api.letsencrypt.org/";
        //public static string BaseURI { get; set; } = "https://acme-v01.api.letsencrypt.org/directory";

        static string UserAgent = "Let's Encrypt Windows Command Line Client";

        static bool PromptYesNo()
        {
            while (true)
            {
                var response = Console.ReadKey(true);
                if (response.Key == ConsoleKey.Y)
                    return true;
                if (response.Key == ConsoleKey.N)
                    return false;
                Console.WriteLine("Please press Y or N.");
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine(UserAgent);

            //var vaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LetsEncrypt");
            //Console.WriteLine("Vault Path: " + vaultPath);

            Console.WriteLine($"ACME Server: {BaseURI}");

            using (var signer = new RS256Signer())
            {
                signer.Init();

                using (var client = new AcmeClient(new Uri(BaseURI), new AcmeServerDirectory(), signer))
                {
                    client.Init();
                    Console.WriteLine("Getting AcmeServerDirectory");
                    client.GetDirectory(true);

                    //client.UserAgent = UserAgent;

                    Console.WriteLine("Calling Register");
                    var registration = client.Register(new string[] { });

                    Console.WriteLine($"Do you agree to {registration.TosLinkUri}? (Y/N)");
                    if (!PromptYesNo())
                        return;

                    Console.WriteLine("Updating Registration");
                    client.UpdateRegistration(true, true);

                    Console.WriteLine("Scanning IIS 7 Site Bindings for Hosts (Elevated Permissions Required)");
                    var hosts = GetHostNames();
                    foreach (var hostName in hosts)
                    {
                        Console.WriteLine($"  - {hostName}");
                    }

                    foreach (var hostName in hosts)
                    {
                        AuthorizeIdentifier(client, hostName.Host, hostName.PhysicalPath);
                    }

                    //_testAuthz_AcmeAuthzFile = $"{_baseLocalStore}\\TestAuthz.acmeAuthz";
                    //using (var fs = new FileStream(_testAuthz_AcmeAuthzFile, FileMode.Create))
                    //{
                    //    authzState.Save(fs);
                    //}
                }
            }

#if DEBUG
            Console.WriteLine("Press enter to continue.");
            Console.ReadLine();
#endif
        }

        static List<SiteHost> GetHostNames()
        {
            var result = new List<SiteHost>();
            using (var iisManager = new ServerManager())
            {
                foreach (var site in iisManager.Sites)
                {
                    foreach (var binding in site.Bindings)
                    {
                        if (!String.IsNullOrEmpty(binding.Host))
                            result.Add(new SiteHost() { Host = binding.Host, PhysicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath });
                    }
                }
            }
            return result;
        }

        static void AuthorizeIdentifier(AcmeClient client, string dnsIdentifier, string webRootPath)
        {
            Console.WriteLine($"Authorizing Identifier {dnsIdentifier}");
            var authzState = client.AuthorizeIdentifier(dnsIdentifier);

            Console.WriteLine("Getting challenge answer from ACME server.");
            var challenge = client.GenerateAuthorizeChallengeAnswer(authzState, AcmeProtocol.CHALLENGE_TYPE_HTTP);
            var answerPath = Path.Combine(webRootPath, challenge.ChallengeAnswer.Key);

            Console.WriteLine($"Writing challenge answer to {answerPath}");
            Directory.CreateDirectory(Path.GetDirectoryName(answerPath));
            File.WriteAllText(answerPath, challenge.ChallengeAnswer.Value);

            try
            {
                Console.WriteLine("Submitting answer");
                // This always throws throw new InvalidOperationException("challenge answer has not been generated"); because the authoState.Challenge list isn't changing for some reason
                //client.SubmitAuthorizeChallengeAnswer(authzState, AcmeProtocol.CHALLENGE_TYPE_HTTP, true);

                // so I pulled the core of SubmitAuthorizeChallengeAnswer into it's own method that I can call directly
                client.SubmitAuthorizeChallengeAnswer(authzState, AcmeProtocol.CHALLENGE_TYPE_HTTP, true);
            }
            finally
            {
                Console.WriteLine("Deleting answer");
                File.Delete(answerPath);
            }

            Console.WriteLine("Refreshing authorization");
            authzState = client.RefreshIdentifierAuthorization(authzState);

            // If I put a break point here, Status is invalid, otherwise it still shows as pending.

            Console.WriteLine($"Authorization RESULT: {authzState.Status}");
        }
    }
}