FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS base

RUN apt update
RUN apt install -y sudo gnupg2 lsb-release

# Install Azure Functions Core Tools
RUN curl -sL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor | sudo tee /etc/apt/trusted.gpg.d/microsoft.gpg > /dev/null && \
    DISTRO=$(lsb_release -cs) && \
    echo "deb [arch=amd64 signed-by=/etc/apt/trusted.gpg.d/microsoft.gpg] https://packages.microsoft.com/repos/microsoft-ubuntu-${DISTRO}-prod ${DISTRO} main" | sudo tee /etc/apt/sources.list.d/dotnetdev.list > /dev/null && \
    sudo apt update -y && \
    sudo apt install -y azure-functions-core-tools-4

FROM base AS final
ENV FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT=1
WORKDIR /app
COPY --exclude=**/bin --exclude=**/obj . .
