cmake_minimum_required(VERSION 3.24)
project(NativeAotSmoke LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_executable(nativeaot_smoke main.cpp)
