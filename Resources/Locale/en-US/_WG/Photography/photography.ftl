wg-photo-camera-examine-film = It has [color=yellow]{$count}[/color] photos left.
wg-photo-camera-examine-size = It takes photos [color=yellow]{$size}x{$size}[/color] meters across.
wg-photo-camera-examine-colour = It prints in colour.
wg-photo-camera-examine-monochrome = It prints in monochrome.

wg-photo-camera-verb-settings = Camera settings
wg-photo-camera-verb-zoom = Zoom
wg-photo-camera-verb-colour = Colour
wg-photo-camera-verb-flash = Flash
wg-photo-camera-verb-labels = Label photos
wg-photo-camera-verb-size-option = {$size}x{$size}
wg-photo-camera-verb-next-size = Next size
wg-photo-camera-verb-colour-option = Colour
wg-photo-camera-verb-monochrome-option = Monochrome
wg-photo-camera-verb-on = On
wg-photo-camera-verb-off = Off
wg-photo-camera-verb-current = Current setting

wg-photo-camera-size-set = The photos will be {$size}x{$size} meters.
wg-photo-camera-monochrome-on = Printing in monochrome.
wg-photo-camera-monochrome-off = Printing in colour.
wg-photo-camera-charging = The flash is still charging!
wg-photo-camera-busy = The image is still developing!
wg-photo-camera-no-film = The film cartridge is used up. Replace it first.
wg-photo-camera-not-empty = The camera still has film in it.
wg-photo-camera-film-loaded = You replace the film cartridge. The camera can take {$count} photos.
wg-photo-camera-film-left = {$count} photos left.
wg-photo-camera-too-far = That's too far away to photograph.
wg-photo-camera-cant-see = You can't see that.
wg-photo-camera-failed = The photo didn't come out.

wg-photo-caption-area = This is a photo of an area of {$size} meters by {$size} meters.
wg-photo-caption-mob = You can also see {$name} on the photo.
wg-photo-caption-mob-hurt = You can also see {$name} on the photo, looking a bit hurt.
wg-photo-caption-holding = {CAPITALIZE(SUBJECT($mob))} {CONJUGATE-BE($mob)} holding {$items}.
wg-photo-caption-item = {INDEFINITE($item)} {$item}
wg-photo-caption-ghost = You can also see a g-g-g-g-ghooooost!

wg-photo-default-name = photo
wg-photo-renamed = photo - '{$name}'
wg-photo-customised-description = {$custom} - {$caption}
wg-photo-customise-title = Customise photo
wg-photo-customise-name = Name (optional)
wg-photo-customise-description = Description (optional)
wg-photo-customise-caption = Caption on the back (optional)
wg-photo-verb-rename = Rename photo
wg-photo-rename-title = Rename photo
wg-photo-rename-prompt = What would you like to label the photo?
wg-photo-write-title = Photo writing
wg-photo-write-prompt = What would you like to write on the back?
wg-photo-write-done = You write on the back of the photo.
wg-photo-examine-back = Written on the back: [italic]{$text}[/italic]
wg-photo-examine-closer = You need to get closer to get a good look at this photo.

wg-photo-window-loading = Developing...
wg-photo-window-blank = This photo is blank.
wg-photo-window-empty = There is no photo in the frame.
wg-photo-window-empty-title = Empty frame
wg-photo-window-back = Written on the back: {$text}

wg-photo-frame-full = The frame already holds a photo.
wg-photo-frame-cut = {CAPITALIZE(THE($user))} cuts the photo out of {THE($frame)}.
wg-photo-frame-name = picture frame
wg-photo-frame-description = Every time you look it makes you laugh.
wg-photo-frame-examine-photo = Use it to look at the photo. Wirecutters cut the photo out.
wg-photo-frame-examine-portrait = The frame and the picture are glued together, but you could slip a photo between the two.

wg-photo-portrait-monkey-name = Mr. Deempisi portrait
wg-photo-portrait-monkey-description = Under the portrait a plaque reads: 'While the meat grinder may not have spared you, fear not. Not one part of you has gone to waste... You were delicious.'
wg-photo-portrait-fruit-name = picture of a fruit
wg-photo-portrait-fruit-description = Ceci n'est pas une orange.
wg-photo-portrait-rat-name = Tom portrait
wg-photo-portrait-rat-description = Jerry the cat is still not amused.

wg-photo-gallery-title = Photo gallery
wg-photo-gallery-print = Print
wg-photo-gallery-delete = Delete
wg-photo-gallery-entry-name = Image {$number} (taken by {$user})
wg-photo-gallery-recorded = Image recorded.
wg-photo-gallery-printed = {CAPITALIZE(THE($user))} spits out a photograph from a narrow slot on its chassis.
wg-photo-gallery-no-charge = Not enough charge to print!

wg-photo-soul-stolen = You feel your essence draining away from having your picture taken!

trait-photographer-name = Photographer
trait-photographer-desc = You carry your camera and personal photo album everywhere you go, and your scrapbooks are legendary among your coworkers.

cmd-blankphoto-desc = Deletes a photo's image for good and blanks every copy of it, in the round and in saved albums.
cmd-blankphoto-help = Usage: {$command} <hash or photo/frame entity ID>. The hash is in the photo admin logs.
cmd-blankphoto-invalid-args = Expected one argument.
cmd-blankphoto-bad-target = {$target} isn't a photo hash or an entity holding a photo.
cmd-blankphoto-success = Blanked photo {$hash}: {$count} copies in the round. Saved copies blank when they next load.

ent-WGCamera = camera
    .desc = A polaroid camera.
ent-WGCameraDetective = detective's camera
    .desc = A silent polaroid camera with extra capacity for crime investigations.
ent-WGCameraPersonal = camera
    .desc = A polaroid camera.
    .suffix = Photographer
ent-WGCameraObscura = camera obscura
    .desc = A polaroid camera, some say it can see ghosts!
ent-WGCameraObscuraBadmin = camera obscura
    .desc = A polaroid camera, some say it can see ghosts! It seems to have an extra magnifier on the end.
    .suffix = Admin
ent-WGCameraFilm = film cartridge
    .desc = A camera film cartridge. Insert it into a camera to reload it.
ent-WGCameraBorg = cyborg photo camera
    .desc = A camera built into a cyborg. It keeps its photos and prints them on demand.
ent-WGBorgModulePhotography = photography cyborg module
ent-WGPhoto = photo
ent-WGPhotoAlbum = photo album
    .desc = A big book used to store photos and mementos.
ent-WGPhotoAlbumBlue = photo album
    .desc = A big book used to store photos and mementos.
    .suffix = Blue
ent-WGPhotoAlbumRed = photo album
    .desc = A big book used to store photos and mementos.
    .suffix = Red
ent-WGPhotoAlbumGreen = photo album
    .desc = A big book used to store photos and mementos.
    .suffix = Green
ent-WGPhotoAlbumPersonal = personal photo album
    .desc = A photographer's own album, filled over many shifts.
    .suffix = Personal
ent-WGBasePictureFrame = picture frame
    .desc = Every time you look it makes you laugh.
ent-WGPictureFrame = picture frame
    .desc = The perfect showcase for your favorite deathtrap memories.
ent-WGPictureFrameShowroom = distinguished crew display
    .desc = A photo frame to commemorate crewmembers that distinguished themselves in the line of duty. WARNING: unauthorized tampering will be severely punished.
ent-WGPictureFrameMonkey = Mr. Deempisi portrait
    .desc = Under the portrait a plaque reads: 'While the meat grinder may not have spared you, fear not. Not one part of you has gone to waste... You were delicious.'
ent-WGPictureFrameFruit = picture of a fruit
    .desc = Ceci n'est pas une orange.
ent-WGPictureFrameRat = Tom portrait
    .desc = Jerry the cat is still not amused.
ent-WGActionPhotoAiCapture = Take photo
    .desc = Take a photo of a spot the station cameras can see.
ent-WGActionPhotoGallery = View photos
    .desc = Look through the photos you have taken.
