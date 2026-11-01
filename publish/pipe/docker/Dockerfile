FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final

# git: Git script repositories, python3: Python scripts (Run Scripts plugin)
RUN apt-get update && \
    apt-get install -y --no-install-recommends git python3 ca-certificates && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY /app .

# workflows, executions, credentials, data protection keys
VOLUME /app/data
# pipe.config, created from _setup/proto on first start
VOLUME /app/_config

# not 80 => errors with RunAsNonRoot
EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080

ENTRYPOINT ["dotnet", "gView.Pipe.Shell.dll"]
