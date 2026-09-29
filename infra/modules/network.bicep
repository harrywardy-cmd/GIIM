// Private network for GIIM. The apps reach SQL, Key Vault and Blob Storage over private endpoints, and go out to
// the internet (Graph, Okta, ServiceDesk Plus) through a NAT gateway with one fixed IP address that other services
// can allow-list.

param name string
param location string
param addressPrefix string
param tags object

var appSubnetPrefix = cidrSubnet(addressPrefix, 24, 0)       // e.g. 10.20.0.0/24: App Service VNet integration
var endpointSubnetPrefix = cidrSubnet(addressPrefix, 27, 8)  // e.g. 10.20.1.0/27: private endpoints

resource outboundIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: 'pip-ng-${name}'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

resource nat 'Microsoft.Network/natGateways@2024-05-01' = {
  name: 'ng-${name}'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: {
    idleTimeoutInMinutes: 4
    publicIpAddresses: [{ id: outboundIp.id }]
  }
}

resource appNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-app-${name}'
  location: location
  tags: tags
}

resource endpointNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-endpoints-${name}'
  location: location
  tags: tags
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${name}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [addressPrefix] }
    subnets: [
      {
        name: 'snet-app'
        properties: {
          addressPrefix: appSubnetPrefix
          natGateway: { id: nat.id }
          networkSecurityGroup: { id: appNsg.id }
          defaultOutboundAccess: false
          delegations: [{ name: 'app-service', properties: { serviceName: 'Microsoft.Web/serverFarms' } }]
        }
      }
      {
        name: 'snet-endpoints'
        properties: {
          addressPrefix: endpointSubnetPrefix
          networkSecurityGroup: { id: endpointNsg.id }
          defaultOutboundAccess: false
          privateEndpointNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

// Private DNS so the usual service names (x.database.windows.net etc.) resolve to private addresses inside the network.
var zoneNames = [
  'privatelink${environment().suffixes.sqlServerHostname}' // 0: SQL
  'privatelink.vaultcore.azure.net' // 1: Key Vault
  'privatelink.blob.${environment().suffixes.storage}' // 2: Blob Storage
]

resource zones 'Microsoft.Network/privateDnsZones@2020-06-01' = [for zone in zoneNames: {
  name: zone
  location: 'global'
  tags: tags
}]

resource zoneLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = [for (zone, i) in zoneNames: {
  parent: zones[i]
  name: 'link-${vnet.name}'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
}]

output appSubnetId string = vnet.properties.subnets[0].id
output endpointSubnetId string = vnet.properties.subnets[1].id
output outboundIpAddress string = outboundIp.properties.ipAddress
output dnsZoneIds object = {
  sql: zones[0].id
  vault: zones[1].id
  blob: zones[2].id
}
