# CMU's civilian vehicles: the emergency lights.
cmu-emergency-lights-mode = { $mode ->
    [1] You switch on the emergency lights. (Slow)
    [2] You speed up the emergency lights. (Medium)
    [3] You speed up the emergency lights. (Fast)
   *[other] You switch off the emergency lights.
}

# Entity names.
ent-CMUActionVehicleEmergencyLights = Toggle emergency lights
    .desc = Cycle the vehicle's red and blue emergency lights between off, slow, medium and fast.
ent-CMUEmergencyLightbarBase = emergency lightbar
    .desc = A red and blue lightbar.
ent-CMUEmergencyLightbarSlow = { ent-CMUEmergencyLightbarBase }
    .desc = { ent-CMUEmergencyLightbarBase.desc }
ent-CMUEmergencyLightbarMedium = { ent-CMUEmergencyLightbarBase }
    .desc = { ent-CMUEmergencyLightbarBase.desc }
ent-CMUEmergencyLightbarFast = { ent-CMUEmergencyLightbarBase }
    .desc = { ent-CMUEmergencyLightbarBase.desc }
