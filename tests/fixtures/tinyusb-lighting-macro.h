// Copyright (c) 2019 Ha Thach (tinyusb.org)
// SPDX-License-Identifier: MIT
// Extracted 2026-10-17 from https://github.com/hathach/tinyusb/blob/master/src/class/hid/hid_device.h
// Complete downloaded header SHA256: cab9b1c4295344f896688e1ea34477bb30fc7e09f1635ce0fced2376a568fc6b
// Microsoft RP2040MacropadHidSample uses this macro in src/usb_descriptors.c.
#define TUD_HID_REPORT_DESC_LIGHTING(report_id) \
  HID_USAGE_PAGE ( HID_USAGE_PAGE_LIGHTING_AND_ILLUMINATION ),\
  HID_USAGE      ( HID_USAGE_LIGHTING_LAMP_ARRAY            ),\
  HID_COLLECTION ( HID_COLLECTION_APPLICATION               ),\
    /* Lamp Array Attributes Report */ \
    HID_REPORT_ID (report_id                                    ) \
    HID_USAGE ( HID_USAGE_LIGHTING_LAMP_ARRAY_ATTRIBUTES_REPORT ),\
    HID_COLLECTION ( HID_COLLECTION_LOGICAL                     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_COUNT                          ),\
      HID_LOGICAL_MIN   ( 0                                                      ),\
      HID_LOGICAL_MAX_N ( 65535, 3                                               ),\
      HID_REPORT_SIZE   ( 16                                                     ),\
      HID_REPORT_COUNT  ( 1                                                      ),\
      HID_FEATURE       ( HID_CONSTANT | HID_VARIABLE | HID_ABSOLUTE             ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BOUNDING_BOX_WIDTH_IN_MICROMETERS   ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BOUNDING_BOX_HEIGHT_IN_MICROMETERS  ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BOUNDING_BOX_DEPTH_IN_MICROMETERS   ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_ARRAY_KIND                     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_MIN_UPDATE_INTERVAL_IN_MICROSECONDS ),\
      HID_LOGICAL_MIN   ( 0                                                      ),\
      HID_LOGICAL_MAX_N ( 2147483647, 3                                          ),\
      HID_REPORT_SIZE   ( 32                                                     ),\
      HID_REPORT_COUNT  ( 5                                                      ),\
      HID_FEATURE       ( HID_CONSTANT | HID_VARIABLE | HID_ABSOLUTE             ),\
    HID_COLLECTION_END ,\
    /* Lamp Attributes Request Report */ \
    HID_REPORT_ID       ( report_id + 1                                     ) \
    HID_USAGE           ( HID_USAGE_LIGHTING_LAMP_ATTRIBUTES_REQUEST_REPORT ),\
    HID_COLLECTION      ( HID_COLLECTION_LOGICAL                            ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_ID             ),\
      HID_LOGICAL_MIN   ( 0                                      ),\
      HID_LOGICAL_MAX_N ( 65535, 3                               ),\
      HID_REPORT_SIZE   ( 16                                     ),\
      HID_REPORT_COUNT  ( 1                                      ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE ),\
    HID_COLLECTION_END ,\
    /* Lamp Attributes Response Report */ \
    HID_REPORT_ID       ( report_id + 2                                      ) \
    HID_USAGE           ( HID_USAGE_LIGHTING_LAMP_ATTRIBUTES_RESPONSE_REPORT ),\
    HID_COLLECTION      ( HID_COLLECTION_LOGICAL                             ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_ID                        ),\
      HID_LOGICAL_MIN   ( 0                                                 ),\
      HID_LOGICAL_MAX_N ( 65535, 3                                          ),\
      HID_REPORT_SIZE   ( 16                                                ),\
      HID_REPORT_COUNT  ( 1                                                 ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE            ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_POSITION_X_IN_MICROMETERS      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_POSITION_Y_IN_MICROMETERS      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_POSITION_Z_IN_MICROMETERS      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_UPDATE_LATENCY_IN_MICROSECONDS ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_PURPOSES                  ),\
      HID_LOGICAL_MIN   ( 0                                                 ),\
      HID_LOGICAL_MAX_N ( 2147483647, 3                                     ),\
      HID_REPORT_SIZE   ( 32                                                ),\
      HID_REPORT_COUNT  ( 5                                                 ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE            ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_LEVEL_COUNT                ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_LEVEL_COUNT              ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_LEVEL_COUNT               ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_LEVEL_COUNT          ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_IS_PROGRAMMABLE                ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INPUT_BINDING                  ),\
      HID_LOGICAL_MIN   ( 0                                                 ),\
      HID_LOGICAL_MAX_N ( 255, 2                                            ),\
      HID_REPORT_SIZE   ( 8                                                 ),\
      HID_REPORT_COUNT  ( 6                                                 ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE            ),\
    HID_COLLECTION_END ,\
    /* Lamp Multi-Update Report */ \
    HID_REPORT_ID       ( report_id + 3                               ) \
    HID_USAGE           ( HID_USAGE_LIGHTING_LAMP_MULTI_UPDATE_REPORT ),\
    HID_COLLECTION      ( HID_COLLECTION_LOGICAL                      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_COUNT               ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_UPDATE_FLAGS        ),\
      HID_LOGICAL_MIN   ( 0                                           ),\
      HID_LOGICAL_MAX   ( 8                                           ),\
      HID_REPORT_SIZE   ( 8                                           ),\
      HID_REPORT_COUNT  ( 2                                           ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_ID                  ),\
      HID_LOGICAL_MIN   ( 0                                           ),\
      HID_LOGICAL_MAX_N ( 65535, 3                                    ),\
      HID_REPORT_SIZE   ( 16                                          ),\
      HID_REPORT_COUNT  ( 8                                           ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_LOGICAL_MIN   ( 0                                           ),\
      HID_LOGICAL_MAX_N ( 255, 2                                      ),\
      HID_REPORT_SIZE   ( 8                                           ),\
      HID_REPORT_COUNT  ( 32                                          ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE      ),\
    HID_COLLECTION_END ,\
    /* Lamp Range Update Report */ \
    HID_REPORT_ID       ( report_id + 4 ) \
    HID_USAGE           ( HID_USAGE_LIGHTING_LAMP_RANGE_UPDATE_REPORT ),\
    HID_COLLECTION      ( HID_COLLECTION_LOGICAL                      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_UPDATE_FLAGS        ),\
      HID_LOGICAL_MIN   ( 0                                           ),\
      HID_LOGICAL_MAX   ( 8                                           ),\
      HID_REPORT_SIZE   ( 8                                           ),\
      HID_REPORT_COUNT  ( 1                                           ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_ID_START            ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_LAMP_ID_END              ),\
      HID_LOGICAL_MIN   ( 0                                           ),\
      HID_LOGICAL_MAX_N ( 65535, 3                                    ),\
      HID_REPORT_SIZE   ( 16                                          ),\
      HID_REPORT_COUNT  ( 2                                           ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_RED_UPDATE_CHANNEL       ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_GREEN_UPDATE_CHANNEL     ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_BLUE_UPDATE_CHANNEL      ),\
      HID_USAGE         ( HID_USAGE_LIGHTING_INTENSITY_UPDATE_CHANNEL ),\
      HID_LOGICAL_MIN   ( 0                                           ),\
      HID_LOGICAL_MAX_N ( 255, 2                                      ),\
      HID_REPORT_SIZE   ( 8                                           ),\
      HID_REPORT_COUNT  ( 4                                           ),\
      HID_FEATURE       ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE      ),\
    HID_COLLECTION_END ,\
    /* Lamp Array Control Report */ \
    HID_REPORT_ID      ( report_id + 5                                ) \
    HID_USAGE          ( HID_USAGE_LIGHTING_LAMP_ARRAY_CONTROL_REPORT ),\
    HID_COLLECTION     ( HID_COLLECTION_LOGICAL                       ),\
      HID_USAGE        ( HID_USAGE_LIGHTING_AUTONOMOUS_MODE     ),\
      HID_LOGICAL_MIN  ( 0                                      ),\
      HID_LOGICAL_MAX  ( 1                                      ),\
      HID_REPORT_SIZE  ( 8                                      ),\
      HID_REPORT_COUNT ( 1                                      ),\
      HID_FEATURE      ( HID_DATA | HID_VARIABLE | HID_ABSOLUTE ),\
    HID_COLLECTION_END ,\
  HID_COLLECTION_END \

// End of extracted upstream macro.
