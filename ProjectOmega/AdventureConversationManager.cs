using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace ProjectOmega
{
    public class AdventureConversationManager
    {
        private DiscordSocketClient discordClient;
        private Dictionary<ulong, AdventureConversation> adventureDictionary = new Dictionary<ulong, AdventureConversation>();

        public AdventureConversationManager(DiscordSocketClient discordClient)
        {
            this.discordClient = discordClient;

            LoadAdventureDictionary();

            discordClient.MessageReceived += OnMessageReceived;
            discordClient.SlashCommandExecuted += OnSlashCommandExecuted;

            Task.Run(AddStartAdventureCommands);
        }

        private void LoadAdventureDictionary()
        {
            foreach (SocketGuild guild in discordClient.Guilds)
            {
                foreach (SocketTextChannel channel in guild.Channels.OfType<SocketTextChannel>())
                {
                    if (AdventureConversation.IsAdventureChannel(channel))
                    {
                        AdventureConversation adventure = new AdventureConversation();
                        adventure.Load(channel);

                        adventureDictionary.Add(channel.Id, adventure);
                    }
                }
            }
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            SocketTextChannel channel = (message.Channel as SocketTextChannel);

            if (AdventureConversation.IsAdventureChannel(channel))
            {
                AdventureConversation adventure = adventureDictionary[message.Channel.Id];

                if (message.Author.Id == adventure.User.Id || message.Author.Id == discordClient.CurrentUser.Id)
                {
                    await adventure.OnMessageReceived(message);
                }
                else
                {
                    Console.Error.WriteLine($"{message.Author.Username} ({message.Author.Id}) is pusting in channel {adventure.Channel.Id}");
                }
            }
        }

        private async Task OnSlashCommandExecuted(SocketSlashCommand command)
        {
            if (command.CommandName == "start-adventure")
            {
                await StartAdventure(command);
            }
        }

        private async Task AddStartAdventureCommands()
        {
            foreach (SocketGuild guild in discordClient.Guilds)
            {
                SlashCommandBuilder guildCommand = new SlashCommandBuilder();
                guildCommand.WithName("start-adventure");
                guildCommand.WithDescription("Commencer une grande aventure");

                try
                {
                    await guild.CreateApplicationCommandAsync(guildCommand.Build());
                }
                catch (ApplicationCommandException exception)
                {
                    // If our command was invalid, we should catch an ApplicationCommandException. This exception contains the path of the error as well as the error message. You can serialize the Error field in the exception to get a visual of where your error is.
                    var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);

                    // You can send this error somewhere or just print it to the console, for this example we're just going to print it.
                    Console.WriteLine(json);
                }
            }
        }

        private async Task StartAdventure(SocketSlashCommand command)
        {
            await command.RespondAsync("Bonne aventure !", ephemeral: true);

            SocketGuild guild = (command.Channel as SocketTextChannel).Guild;
            SocketUser user = command.User;

            AdventureConversation? adventure = adventureDictionary.Values.ToList().FirstOrDefault(a => a.User.Id == user.Id);
            if (adventure != null)
            {
                await adventure.DeleteChanel();
                adventureDictionary.Remove(user.Id);
            }

            adventure = new AdventureConversation();
            await adventure.Create(guild, user);
            adventureDictionary.Add(adventure.Channel.Id, adventure);
            await adventure.Start();
        }
    }
}
