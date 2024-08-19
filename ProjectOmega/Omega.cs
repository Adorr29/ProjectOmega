using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.Text;
using System.Timers;

namespace ProjectOmega
{
    public static class EnvironmentVariable
    {
        public const string DISCORD_TOKEN = nameof(DISCORD_TOKEN);
        public const string OPENAI_TOKEN = nameof(OPENAI_TOKEN);
    }

    public abstract class ConversationBase
    {
        public SocketUser botUser { get; private set; }
        private System.Timers.Timer respondTimer;

        public List<SocketMessage> messages = new List<SocketMessage>();

        public ConversationBase(SocketUser botUser)
        {
            this.botUser = botUser;

            respondTimer = new System.Timers.Timer(TimeSpan.FromSeconds(5));
            respondTimer.AutoReset = false;
            respondTimer.Elapsed += OnRespondTimerElapsed;
        }

        private void OnRespondTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            messages.RemoveAll(m => m.CreatedAt < DateTime.Now.AddDays(-1));

            Respond();
        }

        protected abstract void Respond();

        public bool IsBot(SocketUser user)
        {
            return user.Id == botUser.Id;
        }

        public void RestartRespondTimer()
        {
            respondTimer.Stop();
            respondTimer.Start();
        }
    }

    public class Conversation : ConversationBase
    {
        private ChatClient client;

        private static string getRespondPrompt = "Tu es un assistant dans une conversation de groupe. Tu es connu sous le nom d'Oméga. T'on rôle est d'aider les personnes de ce groupe. Tu ne dois pas répondre si ce n'est pas nécessaire.";

        public Conversation(SocketUser botUser) : base(botUser)
        {
            client = new ChatClient("gpt-4o-mini", Environment.GetEnvironmentVariable(EnvironmentVariable.OPENAI_TOKEN));
        }

        protected override void Respond()
        {
            string respond = GetRespond();
            bool respondValid = ValidateRespond(respond);

            if (respondValid)
            {
                messages[0].Channel.SendMessageAsync(respond);
            }
        }

        private string GetRespond()
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage(getRespondPrompt));

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (IsBot(message.Author))
                {
                    chatMessages.Add(new AssistantChatMessage(messageContent));
                }
                else
                {
                    chatMessages.Add(new UserChatMessage($"{userName} : {messageContent}"));
                }
            }

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages);
            
            return respondChatCompletion.Content[0].Text;
        }

        private bool ValidateRespond(string respond)
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Ton rôle est de vérifier la pertinence de la réponse de l'assistant ainsi que de déterminer s'il aurait dû répondre ou non. Répond par oui ou par non."));

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Voilà la conversation :");

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (message.Author.Id == botUser.Id)
                {
                    sb.AppendLine($"Oméga : {messageContent}");
                }
                else
                {
                    sb.AppendLine($"{userName} : {messageContent}");
                }
            }

            sb.AppendLine();

            sb.AppendLine("Voilà les instructions de l'assistant :");
            sb.AppendLine(getRespondPrompt);

            sb.AppendLine();

            sb.AppendLine("Et voilà la reponse de l'assistant :");
            sb.AppendLine(respond);

            chatMessages.Add(new UserChatMessage(sb.ToString()));

            ChatCompletion validateRespondChatCompletion = client.CompleteChat(chatMessages);

            return string.Equals(validateRespondChatCompletion.Content[0].Text, "oui", StringComparison.OrdinalIgnoreCase);
        }
    }

    public abstract class NpcConversationBase : ConversationBase
    {
        public abstract string NpcPrompt { get; }
        public abstract string ChannelName { get; }

        private ChatClient client;

        public NpcConversationBase(SocketUser botUser) : base(botUser)
        {
            client = new ChatClient("gpt-4o-mini", Environment.GetEnvironmentVariable(EnvironmentVariable.OPENAI_TOKEN));
        }

        protected override void Respond()
        {
            messages[0].Channel.SendMessageAsync(GetRespond());
        }

        private string GetRespond()
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage(NpcPrompt));

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (IsBot(message.Author))
                {
                    chatMessages.Add(new AssistantChatMessage(messageContent));
                }
                else
                {
                    chatMessages.Add(new UserChatMessage($"{userName} : {messageContent}"));
                }
            }

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages);

            return respondChatCompletion.Content[0].Text;
        }
    }

    public class ForgeConversation : NpcConversationBase
    {
        public override string NpcPrompt => "Tu es un forgeron dans un univers Donjons & Dragons";

        public override string ChannelName => "la-forge";

        public ForgeConversation(SocketUser botUser) : base(botUser)
        {
        }
    }

    public class ApothecaryConversation : NpcConversationBase
    {
        public override string NpcPrompt => "Tu es une apothicaire dans un univers Donjons & Dragons";

        public override string ChannelName => "apothicairerie";

        public ApothecaryConversation(SocketUser botUser) : base(botUser)
        {
        }
    }

    public class Omega()
    {
        private DiscordSocketClient client = new DiscordSocketClient(new DiscordSocketConfig() {
            GatewayIntents = GatewayIntents.All
        });

        private Dictionary<ulong, ConversationBase> conversationDictionary = new Dictionary<ulong, ConversationBase>();
        private AdventureConversationManager adventureConversationManager;

        public async Task Start()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable.DISCORD_TOKEN)))
            {
                throw new Exception($"Environment variable \"{nameof(EnvironmentVariable.DISCORD_TOKEN)}\" can't be null or empty");
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable.OPENAI_TOKEN)))
            {
                throw new Exception($"Environment variable \"{nameof(EnvironmentVariable.OPENAI_TOKEN)}\" can't be null or empty");
            }

            Console.WriteLine("Start Oméga");

            await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable(EnvironmentVariable.DISCORD_TOKEN));

            client.Ready += OnReady;
            client.MessageReceived += OnMessageReceived;
            client.Log += OnLog;

            await client.StartAsync();
            await Task.Delay(-1);
        }

        private async Task OnReady()
        {
            adventureConversationManager = new AdventureConversationManager(client);
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            /*if ((message.Channel as SocketTextChannel).PermissionOverwrites)
            {
                AddMessageToConversation(message);
            }*/
        }

        private Task OnLog(LogMessage message)
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }

        private void AddMessageToConversation(SocketMessage newMessage)
        {
            if (!conversationDictionary.TryGetValue(newMessage.Channel.Id, out ConversationBase conversation))
            {
                conversation = new Conversation(client.CurrentUser);
                conversationDictionary.Add(newMessage.Channel.Id, conversation);
            }

            conversation.messages.Add(newMessage);

            if (!conversation.IsBot(newMessage.Author))
            {
                conversation.RestartRespondTimer();
            }
        }
    }
}