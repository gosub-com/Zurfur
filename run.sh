dotnet publish AvaloniaEditor.Browser/AvaloniaEditor.Browser.csproj --configuration Release
scp -r AvaloniaEditor.Browser/bin/Release/net10.0-browser/publish/wwwroot/* root@gosub.com:~/v5/www/zurfur/.

