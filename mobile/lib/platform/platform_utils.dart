import 'dart:io';

import 'package:flutter/foundation.dart';

bool get isDesktopPlatform =>
    !kIsWeb && (Platform.isWindows || Platform.isMacOS || Platform.isLinux);

bool get isMobilePlatform =>
    !kIsWeb && (Platform.isAndroid || Platform.isIOS);
