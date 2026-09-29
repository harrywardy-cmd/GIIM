// A private endpoint for one service, registered in the matching private DNS zone.

param name string
param location string
param subnetId string
param serviceId string
@description('Sub-resource to expose: sqlServer, vault or blob.')
param groupId string
param dnsZoneId string
param tags object

resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    subnet: { id: subnetId }
    privateLinkServiceConnections: [
      {
        name: name
        properties: {
          privateLinkServiceId: serviceId
          groupIds: [groupId]
        }
      }
    ]
  }

  resource dns 'privateDnsZoneGroups' = {
    name: 'default'
    properties: {
      privateDnsZoneConfigs: [{ name: 'zone', properties: { privateDnsZoneId: dnsZoneId } }]
    }
  }
}
