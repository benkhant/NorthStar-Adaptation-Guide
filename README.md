# NorthStar Adaptation Guide

> ⚠ Work in Progress: Hardware prototype complete. Documentation and software modules under active development.

### A Practical Guide to Building and Documenting a Low-Cost Open-Source Optical See-Through AR Headset

This repository documents the adaptation of Project North Star into a low-cost, reproducible, and well-documented optical see-through augmented reality (OST-AR) headset for computing education. The project establishes a transparent hardware and calibration foundation to support future development of interactive AR learning software.

Rather than focusing solely on hardware construction, this project emphasizes systematic documentation of the entire build process to lower financial and technical barriers to classroom deployment and future AR software development.

---

## Why This Project?

Commercial optical see-through AR headsets remain costly, proprietary, and difficult to deploy in classroom settings. This project aims to lower those barriers by providing a transparent, replicable, and well-documented alternative specifically tailored for computing education.

---

## Project Goals

- Adapt an open-source AR headset for educational use  
- Provide clear mechanical assembly documentation  
- Develop a repeatable optics calibration workflow  
- Publish a transparent bill of materials with cost breakdown  
- Enable reproducibility for educators and researchers  
- Establish a hardware foundation for interactive AR learning software

---

## Hardware Assembly

This guide is under active development. The headstrap assembly section will be added in a future update.

Mechanical assembly instructions are available here:

[Hardware Assembly Guide](docs/hardware-assembly.md)

Includes:
- Required components
- Heat insert installation
- Cable routing
- Display mounting
- Troubleshooting notes

---

## Optics Calibration

Calibration documentation is available here:

 [Optics Calibration Guide](docs/optics-calibration.md)

Includes:
- Checkerboard-based alignment procedure
- Distortion correction process
- Representative calibration results
- Troubleshooting guide

---

## 3D-Printable Components

All printable components are located in:

[3D Printing Files](3d-printing-stl)

Each component is provided as an STL file for direct printing.

---

## Cost Transparency

This build prioritizes affordability using:
- Commodity electronics
- 3D-printed structural parts
- Open-source design

Filament costs are calculated using:
- Slicer-reported filament weight (grams)
- Cost per gram (based on spool price)
- Total print cost per component

A detailed bill of materials and cost breakdown spreadsheet will be published in this repository to support reproducibility.

---

## Current Status

- ✅ Hardware prototype constructed
- ✅ Optics calibration workflow established  
- 🚧 Mechanical documentation in progress  
- 🚧 Interactive AR educational software in development  
- 🚧 Classroom pilot evaluation planned  

---

## Software: AR Linked List Activity

An interactive AR learning activity has been developed for visualizing 
linked list pointer operations using image tracking on an Android tablet.

The activity is located in the [`software/`](software/) folder.

### How It Works
Students physically manipulate printed marker cards on a table. 
The tablet camera detects the cards and augments the scene with:
- Blue numbered nodes floating above each node card
- Orange dots above tail cards (outgoing pointers)
- Purple dots above head cards (incoming connections)
- Green arrows for correct pointer connections
- Red arrows for incorrect connections
- Live task instructions guiding students through insertion

### Activity Flow
1. Place all node cards on the table
2. Connect tail and head cards to build the starting list: 10 → 15 → 30 → 45
3. Insert node 20 by moving tail_15 to head_20 and tail_20 to head_30
4. Complete the insertion: 10 → 15 → 20 → 30 → 45

### Requirements
- Unity 2022.3 LTS
- AR Foundation
- Android tablet with ARCore support
- 13 printed marker cards (see `software/Assets/Markers/`)

### Current Status
- ✅ Image tracking with 13 marker cards
- ✅ Dynamic arrow drawing with color feedback
- ✅ Task instruction system
- ✅ Node insertion activity
- 🚧 Pilot evaluation planned
- 🚧 Port to North Star HMD in progress

---

## Acknowledgements

Inspired by Project North Star.

Developed at Bucknell University.
