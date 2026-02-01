param (
    [PArameter(Mandatory=$false)]
    [string]$ConnectionString = "mongodb://host.docker.internal:27017",
    
    [PArameter(Mandatory=$false)]
    [string]$DbName = "eveproxy-tests",

    [PArameter(Mandatory=$false)]
    [string]$HostUrls = "http://+:5000"
)

docker run -it --rm -p 8080:5000 eveproxy --mongoConnection=$ConnectionString --mongoDbName=$DbName --hostUrls=$HostUrls
