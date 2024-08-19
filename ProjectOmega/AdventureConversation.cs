using Discord.Rest;
using Discord.WebSocket;
using Discord;
using OpenAI.Chat;
using YamlDotNet.Serialization;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectOmega
{
    public class AdventureConversation
    {
        public class ChannelData
        {
            public string ChannelType = nameof(AdventureConversation);
            public ulong UserId;
            public string CharacterDescription;
            public ulong StartMessageId;
        }

        private enum Stat
        {
            CreateCharacter,
            Introduction,
            Play
        }

        public ITextChannel Channel { get; private set; }
        public SocketUser User;

        private ChatClient client = new ChatClient("gpt-4o-mini", Environment.GetEnvironmentVariable(EnvironmentVariable.OPENAI_TOKEN));
        private List<IMessage> messages = new List<IMessage>();
        private Stat stat = Stat.CreateCharacter;
        private string characterDescription;

        public async Task<ITextChannel> Create(SocketGuild guild, SocketUser user)
        {
            User = user;
            Channel = await guild.CreateTextChannelAsync($"la-grande-aventure", f => f.Topic = Serialize());

            await Channel.AddPermissionOverwriteAsync(guild.EveryoneRole, OverwritePermissions.DenyAll(Channel));
            await Channel.AddPermissionOverwriteAsync(user, OverwritePermissions.AllowAll(Channel));

            return Channel;
        }

        public async void Load(SocketTextChannel channel)
        {
            Channel = channel;

            ChannelData? channelData = Deserialize();

            User = channel.Guild.GetUser(channelData.UserId);
            characterDescription = channelData.CharacterDescription;
            stat = Stat.Play;

            var channelMessagesPages = channel.GetMessagesAsync(channelData.StartMessageId, Direction.After);

            await foreach (var channelMessagesPage in channelMessagesPages)
            {
                foreach (var channelMessage in channelMessagesPage)
                {
                    messages.Add(channelMessage);
                }
            }

            messages.Reverse();

            //messages.
        }

        private string Serialize()
        {
            ChannelData channelData = new ChannelData()
            {
                UserId = User.Id,
                CharacterDescription = characterDescription,
                StartMessageId = messages.FirstOrDefault()?.Id ?? 0
            };

            return Serialize(channelData);
        }

        public static string Serialize(ChannelData channelData)
        {
            var serializer = new SerializerBuilder().Build();
            return serializer.Serialize(channelData);
        }

        private ChannelData? Deserialize()
        {
            return Deserialize(Channel.Topic);
        }

        public static ChannelData? Deserialize(string channelDataYaml)
        {
            if (channelDataYaml == null)
                return null;

            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<ChannelData>(channelDataYaml);
        }

        public static ChannelData? Deserialize(ITextChannel channel)
        {
            return Deserialize(channel.Topic);
        }

        public static bool IsAdventureChannel(ITextChannel channel)
        {
            ChannelData? channelData = Deserialize(channel);

            return channelData != null && channelData.ChannelType == nameof(AdventureConversation);
        }

        public async Task Start()
        {
            await Channel.SendMessageAsync("Bonjour aventurier et bienvenu dans cette nouvelle aventure.");
            await Channel.SendMessageAsync("Tout d'abord commençons par créer ton personnage. Dis moi qui désires-tu incarner.");
        }

        public async Task DeleteChanel()
        {
            await Channel.DeleteAsync();
        }

        public async Task OnMessageReceived(SocketMessage message)
        {
            messages.Add(message);

            if (message.Author.Id == User.Id)
            {
                if (stat == Stat.CreateCharacter)
                {
                    await CreateCharacterConversation();
                }
                else if (stat == Stat.Play)
                {
                    await PlayConversation();
                }
            }
        }

        private async Task CreateCharacterConversation()
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Tu es maitre du jeu d'un univers médiévale fantastique. Tu dois aider le joueur à créer son personnage. Puis écrit un résumé de son personnage, son histoire, ses objectifs."));

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (message.Author.Id == User.Id)
                {
                    chatMessages.Add(new UserChatMessage(messageContent));
                }
                else
                {
                    chatMessages.Add(new AssistantChatMessage(messageContent));
                }
            }

            ChatCompletionOptions options = new ChatCompletionOptions();
            ChatTool tool = ChatTool.CreateFunctionTool("create_character", "Create a character for the game", BinaryData.FromString("""
                {
                  "type": "object",
                  "properties": {
                    "character_description": {
                      "type": "string",
                      "description": "The description of the character"
                    }
                  },
                  "required": [
                    "character_description"
                  ],
                  "additionalProperties": false
                }
                """));
            options.Tools.Add(tool);

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages, options);

            if (respondChatCompletion.ToolCalls.Count > 0)
            {
                string functionArguments = respondChatCompletion.ToolCalls.Single().FunctionArguments;
                JObject functionArgumentsJson = (JObject)JsonConvert.DeserializeObject(functionArguments);
                characterDescription = functionArgumentsJson["character_description"].Value<string>();
                Console.WriteLine(characterDescription);
                await Channel.ModifyAsync(f => f.Topic = Serialize());
                await Channel.SendMessageAsync("Très bien, nous allons pouvoir commencer ton aventure.");
                stat = Stat.Introduction;

                await IntroductionConversation();
            }
            else
            {
                string respond = respondChatCompletion.Content[0].Text;
                await SendMessageAsyncSplit(respond);
            }
        }

        private async Task IntroductionConversation()
        {
            messages.Clear();

            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Tu es le maitre du jeu de l'univers médiévale fantastique. Tu dois créer l'introduction et lancer cette nouvelle aventure."));
            chatMessages.Add(new SystemChatMessage($"Le personnage du joueur : {characterDescription}"));

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages);

            string respond = respondChatCompletion.Content[0].Text;
            await SendMessageAsyncSplit(respond);

            stat = Stat.Play;
        }

        private async Task PlayConversation()
        {
            if (Deserialize().StartMessageId == messages[0].Id)
            {
                await Channel.ModifyAsync(f => f.Topic = Serialize());
            }

            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Tu es le maitre du jeu de l'univers médiévale fantastique."));
            chatMessages.Add(new SystemChatMessage($"Le personnage du joueur : {characterDescription}"));

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (message.Author.Id == User.Id)
                {
                    chatMessages.Add(new UserChatMessage(messageContent));
                }
                else
                {
                    chatMessages.Add(new AssistantChatMessage(messageContent));
                }
            }

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages);

            string respond = respondChatCompletion.Content[0].Text;
            await SendMessageAsyncSplit(respond);
        }

        private async Task SendMessageAsyncSplit(string message)
        {
            string[] splitedMessage = message.Split(new string[] { "\n\n", "\r\n\r\n", "\r\r" }, StringSplitOptions.None);

            foreach (string splitedMessagePart in splitedMessage)
            {
                await Channel.SendMessageAsync(splitedMessagePart);
            }
        }
    }
}
